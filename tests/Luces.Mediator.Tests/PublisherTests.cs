using Microsoft.Extensions.DependencyInjection;

namespace Luces.Mediator.Tests;

public sealed record StockMoved(decimal Delta) : INotification;

public sealed class RecordingHandler<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public List<TNotification> Received { get; } = new();

    public Task Handle(TNotification notification, CancellationToken ct)
    {
        Received.Add(notification);
        return Task.CompletedTask;
    }
}

public sealed class FailingHandler<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public Task Handle(TNotification notification, CancellationToken ct)
        => throw new InvalidOperationException("handler failed");
}

/// <summary>Cancels the shared token source inside Handle, then returns normally.</summary>
public sealed class SelfCancellingHandler : INotificationHandler<StockMoved>
{
    private readonly CancellationTokenSource _cts;
    private int _calls;

    public SelfCancellingHandler(CancellationTokenSource cts) => _cts = cts;

    public int Calls => Volatile.Read(ref _calls);

    public Task Handle(StockMoved notification, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        _cts.Cancel();
        return Task.CompletedTask;
    }
}

public sealed class PublisherTests
{
    private static IServiceProvider Build(Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Constructor_rejects_null_service_provider()
    {
        Assert.Throws<ArgumentNullException>(() => new Publisher(null!));
    }

    [Fact]
    public async Task Publish_rejects_null_notification()
    {
        var publisher = new Publisher(Build());

        await Assert.ThrowsAsync<ArgumentNullException>(() => publisher.Publish<StockMoved>(null!, CancellationToken.None));
    }

    [Fact]
    public async Task PublishParallel_rejects_null_notification()
    {
        var publisher = new Publisher(Build());

        await Assert.ThrowsAsync<ArgumentNullException>(() => publisher.PublishParallel<StockMoved>(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Publish_dispatches_to_all_registered_handlers_in_order()
    {
        var first = new RecordingHandler<StockMoved>();
        var second = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(Build(s =>
        {
            s.AddTransient<INotificationHandler<StockMoved>>(_ => first);
            s.AddTransient<INotificationHandler<StockMoved>>(_ => second);
        }));

        var notification = new StockMoved(1.5m);
        await publisher.Publish(notification);

        Assert.Same(notification, first.Received.Single());
        Assert.Same(notification, second.Received.Single());
    }

    [Fact]
    public async Task Publish_with_no_handlers_completes_silently()
    {
        var publisher = new Publisher(Build());

        await publisher.Publish(new StockMoved(1m));
    }

    [Fact]
    public async Task Publish_with_pre_cancelled_token_throws_without_dispatching_handlers()
    {
        var handler = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(Build(s => s.AddTransient<INotificationHandler<StockMoved>>(_ => handler)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.Publish(new StockMoved(1m), cts.Token));
        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task Publish_stops_scheduling_handlers_after_cancellation()
    {
        var first = new BlockingUntilCancelledHandler<StockMoved>();
        var second = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(Build(s =>
        {
            s.AddTransient<INotificationHandler<StockMoved>>(_ => first);
            s.AddTransient<INotificationHandler<StockMoved>>(_ => second);
        }));
        using var cts = new CancellationTokenSource();

        var publishTask = publisher.Publish(new StockMoved(1m), cts.Token);
        cts.CancelAfter(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publishTask);

        Assert.Equal(1, first.Calls);
        Assert.Empty(second.Received);
    }

    [Fact]
    public async Task Publish_throws_when_handler_cancels_token_but_returns_successfully()
    {
        // Documented behavior change: a handler that cancels the caller's token but returns
        // normally still results in OperationCanceledException from the final boundary check.
        using var cts = new CancellationTokenSource();
        var handler = new SelfCancellingHandler(cts);
        var publisher = new Publisher(Build(s => s.AddTransient<INotificationHandler<StockMoved>>(_ => handler)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.Publish(new StockMoved(1m), cts.Token));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task PublishParallel_dispatches_to_all_handlers()
    {
        var first = new RecordingHandler<StockMoved>();
        var second = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(Build(s =>
        {
            s.AddTransient<INotificationHandler<StockMoved>>(_ => first);
            s.AddTransient<INotificationHandler<StockMoved>>(_ => second);
        }));

        var notification = new StockMoved(2m);
        await publisher.PublishParallel(notification);

        Assert.Same(notification, first.Received.Single());
        Assert.Same(notification, second.Received.Single());
    }

    [Fact]
    public async Task PublishParallel_checks_token_at_entry_even_with_no_handlers()
    {
        // The entry check happens before the empty-handler short-circuit: a pre-cancelled
        // token throws even when no handlers are registered.
        var publisher = new Publisher(Build());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishParallel(new StockMoved(1m), cts.Token));
    }

    [Fact]
    public async Task PublishParallel_with_pre_cancelled_token_throws_without_dispatching()
    {
        var handler = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(Build(s => s.AddTransient<INotificationHandler<StockMoved>>(_ => handler)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishParallel(new StockMoved(1m), cts.Token));
        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task PublishParallel_runs_handlers_concurrently()
    {
        // Two handlers that both wait for each other's signals can only complete if the publisher
        // schedules them in parallel.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new Publisher(Build(s =>
        {
            s.AddTransient<INotificationHandler<StockMoved>>(_ => new GatedHandler(gate, expectedStarts: 2));
            s.AddTransient<INotificationHandler<StockMoved>>(_ => new GatedHandler(gate, expectedStarts: 2));
        }));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await publisher.PublishParallel(new StockMoved(1m), cts.Token);

        Assert.Equal(2, GatedHandler.CompletedCount);
    }

    [Fact]
    public async Task PublishParallel_wraps_handler_failures_in_notification_handler_exceptions()
    {
        var good = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(Build(s =>
        {
            s.AddTransient<INotificationHandler<StockMoved>>(_ => good);
            s.AddTransient<INotificationHandler<StockMoved>, FailingHandler<StockMoved>>();
        }));

        var ex = await Assert.ThrowsAsync<AggregateException>(() => publisher.PublishParallel(new StockMoved(1m)));

        var handlerExceptions = ex.InnerExceptions.OfType<NotificationHandlerException>().ToArray();
        var failure = Assert.Single(handlerExceptions);
        Assert.Equal(typeof(FailingHandler<StockMoved>), failure.HandlerType);
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        // The successful handler still ran to completion.
        Assert.Single(good.Received);
    }

    [Fact]
    public async Task PublishParallel_aggregates_multiple_handler_failures()
    {
        var publisher = new Publisher(Build(s =>
        {
            s.AddTransient<INotificationHandler<StockMoved>, FailingHandler<StockMoved>>();
            s.AddTransient<INotificationHandler<StockMoved>, FailingHandler<StockMoved>>();
        }));

        var ex = await Assert.ThrowsAsync<AggregateException>(() => publisher.PublishParallel(new StockMoved(1m)));

        Assert.Equal(2, ex.InnerExceptions.OfType<NotificationHandlerException>().Count());
    }

    [Fact]
    public async Task PublishParallel_prefers_real_failures_over_caller_cancellation()
    {
        // Documented: when caller cancellation and a real error happen together, the real error wins.
        var publisher = new Publisher(Build(s =>
        {
            s.AddTransient<INotificationHandler<StockMoved>, FailingHandler<StockMoved>>();
        }));
        using var cts = new CancellationTokenSource();

        var publishTask = publisher.PublishParallel(new StockMoved(1m), cts.Token);
        cts.Cancel();
        var ex = await Assert.ThrowsAsync<AggregateException>(() => publishTask);

        Assert.IsType<NotificationHandlerException>(ex.InnerExceptions.Single());
    }

    [Fact]
    public async Task PublishParallel_ignores_handler_cancellation_when_token_is_cancelled()
    {
        // A handler observing the cancelled token and throwing OCE is filtered out as
        // cooperative cancellation, not a failure.
        var handler = new BlockingUntilCancelledHandler<StockMoved>();
        var publisher = new Publisher(Build(s => s.AddTransient<INotificationHandler<StockMoved>>(_ => handler)));
        using var cts = new CancellationTokenSource();

        var publishTask = publisher.PublishParallel(new StockMoved(1m), cts.Token);
        cts.CancelAfter(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publishTask);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task PublishParallel_with_isolated_scopes_dispatches_to_all_handlers()
    {
        var first = new RecordingHandler<StockMoved>();
        var second = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(
            Build(s =>
            {
                s.AddTransient<INotificationHandler<StockMoved>>(_ => first);
                s.AddTransient<INotificationHandler<StockMoved>>(_ => second);
            }),
            new PublisherOptions { IsolateParallelHandlerScopes = true });

        await publisher.PublishParallel(new StockMoved(1m));

        Assert.Same(first.Received.Single(), second.Received.Single());
    }

    [Fact]
    public async Task PublishParallel_with_isolated_scopes_gives_each_handler_its_own_scoped_dependency()
    {
        // The whole point of isolation: two handlers resolving the same scoped service must see
        // different instances (a shared DbContext would blow up under concurrency).
        var publisher = new Publisher(
            Build(s =>
            {
                s.AddScoped<ScopedTracker>();
                s.AddTransient<INotificationHandler<StockMoved>, ScopedObservingHandler>();
                s.AddTransient<INotificationHandler<StockMoved>, ScopedObservingHandler>();
            }),
            new PublisherOptions { IsolateParallelHandlerScopes = true });

        var notification = new StockMoved(1m);
        await publisher.PublishParallel(notification);

        var seen = ScopedObservingHandler.ScopedInstances;
        Assert.Equal(2, seen.Count);
        Assert.NotSame(seen[0], seen[1]);
        // Both scoped instances observe the same notification.
        Assert.All(seen, tracker => Assert.Same(notification, tracker.LastNotification));
    }

    [Fact]
    public async Task PublishParallel_with_isolated_scopes_disposes_scoped_dependencies()
    {
        var publisher = new Publisher(
            Build(s =>
            {
                s.AddScoped<DisposableTracker>();
                s.AddTransient<INotificationHandler<StockMoved>, DisposableObservingHandler>();
            }),
            new PublisherOptions { IsolateParallelHandlerScopes = true });

        await publisher.PublishParallel(new StockMoved(1m));

        Assert.NotNull(DisposableObservingHandler.Tracker);
        Assert.True(DisposableObservingHandler.Tracker.Disposed);
    }

    [Fact]
    public async Task PublishParallel_with_isolated_scopes_shares_singleton_handlers()
    {
        // Isolation protects scoped/transient dependencies; a singleton handler instance is still
        // shared across parallel handlers.
        var handler = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(
            Build(s => s.AddSingleton<INotificationHandler<StockMoved>>(_ => handler)),
            new PublisherOptions { IsolateParallelHandlerScopes = true });

        await publisher.PublishParallel(new StockMoved(1m));

        Assert.Single(handler.Received);
    }

    [Fact]
    public async Task PublishParallel_isolated_scopes_require_scope_factory()
    {
        // A bare IServiceProvider without IServiceScopeFactory cannot create child scopes.
        var publisher = new Publisher(new BareServiceProvider(), new PublisherOptions { IsolateParallelHandlerScopes = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishParallel(new StockMoved(1m)));
    }

    [Fact]
    public async Task PublishParallel_with_isolated_scopes_pre_cancelled_token_throws_without_dispatching()
    {
        var handler = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(
            Build(s => s.AddTransient<INotificationHandler<StockMoved>>(_ => handler)),
            new PublisherOptions { IsolateParallelHandlerScopes = true });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishParallel(new StockMoved(1m), cts.Token));

        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task PublishParallel_with_isolated_scopes_wraps_handler_failures()
    {
        var good = new RecordingHandler<StockMoved>();
        var publisher = new Publisher(
            Build(s =>
            {
                s.AddTransient<INotificationHandler<StockMoved>>(_ => good);
                s.AddTransient<INotificationHandler<StockMoved>, FailingHandler<StockMoved>>();
            }),
            new PublisherOptions { IsolateParallelHandlerScopes = true });

        var ex = await Assert.ThrowsAsync<AggregateException>(() => publisher.PublishParallel(new StockMoved(1m)));

        var failure = Assert.Single(ex.InnerExceptions.OfType<NotificationHandlerException>());
        Assert.Equal(typeof(FailingHandler<StockMoved>), failure.HandlerType);
        Assert.Single(good.Received);
    }

    [Fact]
    public async Task PublishParallel_with_isolated_scopes_surfaces_resolution_failure_raw()
    {
        // The handler factory throws before any handler instance exists: no HandlerType to attach,
        // so the raw exception propagates unwrapped — the same contract as the non-isolated path,
        // where eager activation on the caller scope throws raw before any dispatch.
        var publisher = new Publisher(
            Build(s =>
            {
                s.AddTransient<INotificationHandler<StockMoved>, FailingHandler<StockMoved>>();
                s.AddTransient<INotificationHandler<StockMoved>>(_ => throw new InvalidOperationException("resolution blew up"));
            }),
            new PublisherOptions { IsolateParallelHandlerScopes = true });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishParallel(new StockMoved(1m)));

        Assert.Equal("resolution blew up", ex.Message);
    }

    private sealed class BareServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class ScopedTracker
    {
        public INotification? LastNotification { get; set; }
    }

    private sealed class ScopedObservingHandler : INotificationHandler<StockMoved>
    {
        public static readonly List<ScopedTracker> ScopedInstances = new();

        private readonly ScopedTracker _tracker;

        public ScopedObservingHandler(ScopedTracker tracker) => _tracker = tracker;

        public Task Handle(StockMoved notification, CancellationToken ct)
        {
            lock (ScopedInstances)
            {
                ScopedInstances.Add(_tracker);
            }
            _tracker.LastNotification = notification;
            return Task.CompletedTask;
        }
    }

    private sealed class DisposableTracker : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class DisposableObservingHandler : INotificationHandler<StockMoved>
    {
        private readonly DisposableTracker _tracker;

        public DisposableObservingHandler(DisposableTracker tracker) => _tracker = tracker;

        public Task Handle(StockMoved notification, CancellationToken ct)
        {
            Tracker = _tracker;
            return Task.CompletedTask;
        }

        public static DisposableTracker? Tracker { get; private set; }
    }

    private sealed class GatedHandler : INotificationHandler<StockMoved>
    {
        private static int s_completed;

        public static int CompletedCount => Volatile.Read(ref s_completed);

        private readonly TaskCompletionSource _gate;
        private readonly int _expectedStarts;

        public GatedHandler(TaskCompletionSource gate, int expectedStarts)
        {
            _gate = gate;
            _expectedStarts = expectedStarts;
        }

        public async Task Handle(StockMoved notification, CancellationToken ct)
        {
            // Wait until all parallel instances are inside Handle; only possible with
            // concurrent scheduling.
            if (Interlocked.Increment(ref s_started) == _expectedStarts)
            {
                _gate.SetResult();
            }

            await _gate.Task.WaitAsync(ct);
            Interlocked.Increment(ref s_completed);
        }

        private static int s_started;
    }

    /// <summary>A handler that blocks until the token is cancelled, then throws OCE.</summary>
    private sealed class BlockingUntilCancelledHandler<TNotification> : INotificationHandler<TNotification>
        where TNotification : INotification
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task Handle(TNotification notification, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.Delay(Timeout.Infinite, ct);
        }
    }
}
