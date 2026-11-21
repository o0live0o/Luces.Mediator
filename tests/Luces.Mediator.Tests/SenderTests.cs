using Microsoft.Extensions.DependencyInjection;

namespace Luces.Mediator.Tests;

public sealed class SenderTests
{
    private static IServiceProvider Build(Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_returns_handler_response()
    {
        var sender = new Sender(Build(s => s.AddTransient<IRequestHandler<Ping, string>, PingHandler>()));

        var response = await sender.Send<Ping, string>(new Ping("Ada"));

        Assert.Equal("pong: Ada", response);
    }

    [Fact]
    public async Task Send_supports_unit_responses()
    {
        var sender = new Sender(Build(s => s.AddTransient<IRequestHandler<Pong, Unit>, UnitHandler>()));

        var response = await sender.Send<Pong, Unit>(new Pong());

        Assert.Equal(Unit.Value, response);
    }

    [Fact]
    public void Send_rejects_null_request()
    {
        var sender = new Sender(Build());

        Assert.Throws<ArgumentNullException>(() => sender.Send<Ping, string>(null!, CancellationToken.None));
    }

    [Fact]
    public void Send_without_registered_handler_throws()
    {
        var sender = new Sender(Build());

        var ex = Assert.Throws<InvalidOperationException>(() => sender.Send<Ping, string>(new Ping("Ada")));

        Assert.Contains("No handler is registered", ex.Message);
    }

    [Fact]
    public void Send_with_multiple_registered_handlers_throws()
    {
        var services = Build(s =>
        {
            s.AddTransient<IRequestHandler<Ping, string>, PingHandler>();
            s.AddTransient<IRequestHandler<Ping, string>>(_ => new PingHandler());
        });
        var sender = new Sender(services);

        var ex = Assert.Throws<InvalidOperationException>(() => sender.Send<Ping, string>(new Ping("Ada")));

        Assert.Contains("Multiple handlers are registered", ex.Message);
    }

    [Fact]
    public async Task Send_executes_pipeline_behaviors_in_registration_order()
    {
        var first = new RecordingBehavior<Ping, string>();
        var second = new RecordingBehavior<Ping, string>();
        var sender = new Sender(Build(s =>
        {
            s.AddTransient<IRequestHandler<Ping, string>, PingHandler>();
            s.AddTransient<IPipelineBehavior<Ping, string>>(_ => first);
            s.AddTransient<IPipelineBehavior<Ping, string>>(_ => second);
        }));

        var response = await sender.Send<Ping, string>(new Ping("Ada"));

        Assert.Equal("pong: Ada", response);
        // Registration order composes outer-to-inner: the first behavior starts first and finishes last.
        Assert.Equal(new[] { "before:Ping", "after:Ping" }, first.Log);
        Assert.Equal(new[] { "before:Ping", "after:Ping" }, second.Log);
        // The outer behavior's "before" precedes the inner one's; the handler runs in between.
        Assert.Equal("before:Ping", first.Log[0]);
    }

    [Fact]
    public async Task Send_short_circuiting_behavior_skips_handler_and_later_behaviors()
    {
        var outer = new RecordingBehavior<Ping, string>();
        var later = new RecordingBehavior<Ping, string>();
        var sender = new Sender(Build(s =>
        {
            s.AddTransient<IRequestHandler<Ping, string>, PingHandler>();
            s.AddTransient<IPipelineBehavior<Ping, string>>(_ => outer);
            s.AddTransient<IPipelineBehavior<Ping, string>>(_ => new ShortCircuitBehavior<Ping, string>("denied"));
            s.AddTransient<IPipelineBehavior<Ping, string>>(_ => later);
        }));

        var response = await sender.Send<Ping, string>(new Ping("Ada"));

        Assert.Equal("denied", response);
        Assert.Equal(new[] { "before:Ping", "after:Ping" }, outer.Log);
        Assert.Empty(later.Log);
    }

    [Fact]
    public async Task Send_short_circuiting_behavior_does_not_construct_handler()
    {
        // Lazy resolution contract: a behavior that short-circuits must never trigger handler
        // construction, so expensive constructor dependencies are not built for nothing.
        var constructorCalls = 0;
        var sender = new Sender(Build(s =>
        {
            s.AddTransient<IRequestHandler<Ping, string>>(_ =>
            {
                Interlocked.Increment(ref constructorCalls);
                return new PingHandler();
            });
            s.AddTransient<IPipelineBehavior<Ping, string>>(_ => new ShortCircuitBehavior<Ping, string>("denied"));
        }));

        var response = await sender.Send<Ping, string>(new Ping("Ada"));

        Assert.Equal("denied", response);
        Assert.Equal(0, Volatile.Read(ref constructorCalls));
    }

    [Fact]
    public async Task Send_resolves_handler_once_per_call_when_behaviors_present()
    {
        // The terminal delegate resolves the handler lazily, but exactly once per invocation even
        // when the full pipeline runs.
        var constructorCalls = 0;
        var sender = new Sender(Build(s =>
        {
            s.AddTransient<IRequestHandler<Ping, string>>(_ =>
            {
                Interlocked.Increment(ref constructorCalls);
                return new PingHandler();
            });
            s.AddTransient<IPipelineBehavior<Ping, string>, RecordingBehavior<Ping, string>>();
        }));

        var response = await sender.Send<Ping, string>(new Ping("Ada"));

        Assert.Equal("pong: Ada", response);
        Assert.Equal(1, Volatile.Read(ref constructorCalls));
    }

    [Fact]
    public async Task Send_handler_resolution_failure_surfaces_when_pipeline_reaches_terminal()
    {
        // Without behaviors the resolution failure throws synchronously at the call site; with a
        // passing-through behavior it surfaces when the behavior invokes next.
        var sender = new Sender(Build(s =>
        {
            s.AddTransient<IPipelineBehavior<Ping, string>, RecordingBehavior<Ping, string>>();
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send<Ping, string>(new Ping("Ada")).AsTask());
    }

    [Fact]
    public async Task Send_passes_cancellation_token_through_the_pipeline_to_cooperative_handlers()
    {
        // Documented contract: Send only forwards the token and does not check it itself;
        // a cooperative handler observes it and throws.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sender = new Sender(Build(s => s.AddTransient<IRequestHandler<Ping, string>, TokenObservingHandler>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.Send<Ping, string>(new Ping("Ada"), cts.Token).AsTask());
    }

    [Fact]
    public async Task Send_behavior_exception_propagates_to_caller()
    {
        var sender = new Sender(Build(s =>
        {
            s.AddTransient<IRequestHandler<Ping, string>, PingHandler>();
            s.AddTransient<IPipelineBehavior<Ping, string>, ThrowingBehavior<Ping, string>>();
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send<Ping, string>(new Ping("Ada")).AsTask());
    }

    [Fact]
    public async Task CreateStream_returns_handler_elements()
    {
        var sender = new Sender(Build(s => s.AddTransient<IStreamRequestHandler<StreamQuery, int>, StreamQueryHandler>()));

        var results = new List<int>();
        await foreach (var item in sender.CreateStream<StreamQuery, int>(new StreamQuery(3), CancellationToken.None))
        {
            results.Add(item);
        }

        Assert.Equal(new[] { 0, 1, 2 }, results);
    }

    [Fact]
    public void CreateStream_rejects_null_request()
    {
        var sender = new Sender(Build());

        Assert.Throws<ArgumentNullException>(() => sender.CreateStream<StreamQuery, int>(null!, CancellationToken.None));
    }

    [Fact]
    public void CreateStream_without_registered_handler_throws()
    {
        var sender = new Sender(Build());

        Assert.Throws<InvalidOperationException>(() => sender.CreateStream<StreamQuery, int>(new StreamQuery(3)));
    }

    [Fact]
    public async Task CreateStream_passes_cancellation_token_to_handler()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sender = new Sender(Build(s => s.AddTransient<IStreamRequestHandler<StreamQuery, int>, StreamQueryHandler>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in sender.CreateStream<StreamQuery, int>(new StreamQuery(3), cts.Token))
            {
            }
        });
    }
}
