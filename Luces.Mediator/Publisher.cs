using System.Collections;
using Microsoft.Extensions.DependencyInjection;

namespace Luces.Mediator;

/// <summary>
/// The default <see cref="IPublisher"/> implementation. By default all parallel handlers share
/// the caller's DI scope; pass <see cref="PublisherOptions"/> when constructing to give each
/// handler its own child scope.
/// </summary>
public sealed class Publisher : IPublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly bool _isolateParallelHandlerScopes;

    /// <summary>Creates the publisher with default options (parallel handlers share the caller's scope).</summary>
    /// <param name="serviceProvider">The service provider used to resolve handlers; may be a scoped or root provider.</param>
    public Publisher(IServiceProvider serviceProvider) : this(serviceProvider, null)
    {
    }

    /// <summary>Creates the publisher.</summary>
    /// <param name="serviceProvider">The service provider used to resolve handlers; may be a scoped or root provider.</param>
    /// <param name="options">The publishing options; null is equivalent to defaults.</param>
    public Publisher(IServiceProvider serviceProvider, PublisherOptions? options)
    {
        Guard.NotNull(serviceProvider);

        _serviceProvider = serviceProvider;
        _isolateParallelHandlerScopes = options?.IsolateParallelHandlerScopes == true;
        _scopeFactory = _isolateParallelHandlerScopes
            ? serviceProvider.GetService<IServiceScopeFactory>()
            : null;
    }

    /// <summary>Publishes the notification serially in DI registration order; checks the cancellation token at entry, before scheduling each handler, and after all complete.</summary>
    public async Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
    {
        Guard.NotNull(notification);

        // Serial publishing observes the caller's token at every dispatch boundary: a pre-cancelled
        // token skips handler resolution entirely, cancellation stops scheduling further handlers, and
        // a final check matches PublishParallel's post-completion behavior. Started handlers must still
        // observe the token cooperatively; this does not force-terminate them.
        ct.ThrowIfCancellationRequested();

        var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>();
        foreach (var handler in handlers)
        {
            ct.ThrowIfCancellationRequested();
            await handler.Handle(notification, ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Publishes the notification to all handlers concurrently; by default handlers share the caller's scope, and with <see cref="PublisherOptions.IsolateParallelHandlerScopes"/> each handler gets its own child scope.</summary>
    public async Task PublishParallel<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
    {
        Guard.NotNull(notification);
        ct.ThrowIfCancellationRequested();

        if (_isolateParallelHandlerScopes)
        {
            await PublishParallelIsolated(notification, ct).ConfigureAwait(false);
            return;
        }

        var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>().ToArray();

        if (handlers.Length == 0)
            return;

        var failures = await Task.WhenAll(handlers.Select(handler => ExecuteHandler(handler, notification, ct))).ConfigureAwait(false);

        ThrowIfFailures(failures, ct);
    }

    private async Task PublishParallelIsolated<TNotification>(TNotification notification, CancellationToken ct) where TNotification : INotification
    {
        if (_scopeFactory is null)
            throw new InvalidOperationException(
                $"IsolateParallelHandlerScopes requires an {nameof(IServiceScopeFactory)} registered in the service provider.");

        // Handlers are not enumerated on the caller scope: each task resolves the handler at its own
        // index from its own child scope, so scoped dependencies are never shared across handlers. A
        // dedicated counting scope learns the registration count first; it activates every handler
        // once (DI factories run an extra time), mirroring the non-isolated path where the eager
        // ToArray() activates handlers before dispatch too. A throwing factory therefore surfaces
        // raw here, exactly like the documented non-wrapped resolution-failure contract.
        var count = CountHandlerRegistrations<TNotification>();

        if (count == 0)
            return;

        var failures = await Task.WhenAll(Enumerable.Range(0, count)
            .Select(index => ExecuteIsolatedHandler(index, notification, ct)))
            .ConfigureAwait(false);

        ThrowIfFailures(failures, ct);
    }

    private async Task<Exception?> ExecuteIsolatedHandler<TNotification>(int index, TNotification notification, CancellationToken ct) where TNotification : INotification
    {
        await using var scope = _scopeFactory!.CreateAsyncScope();

        INotificationHandler<TNotification>? handler = null;
        try
        {
            var remaining = index;
            foreach (var candidate in scope.ServiceProvider.GetServices<INotificationHandler<TNotification>>())
            {
                if (remaining-- == 0)
                {
                    handler = candidate;
                    break;
                }
            }

            if (handler is null)
                throw new InvalidOperationException($"No handler is registered at index {index} for '{typeof(INotificationHandler<TNotification>).FullName}'.");

            await handler.Handle(notification, ct).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            // With a resolved handler the failure is typed and aggregated exactly like the
            // shared-scope path; a null handler means the DI factory threw before producing an
            // instance, so there is no type to attach and the raw exception is surfaced unwrapped.
            return handler is null ? ex : new NotificationHandlerException(handler.GetType(), ex);
        }
    }

    private int CountHandlerRegistrations<TNotification>() where TNotification : INotification
    {
        // Counting scope: activates every registration once purely to learn the count, then throws
        // it away. Factories therefore run one extra time; a throwing factory surfaces raw at
        // dispatch entry, matching the non-isolated path where eager activation throws raw too.
        using var scope = _scopeFactory!.CreateScope();

        var count = 0;
        foreach (var _ in scope.ServiceProvider.GetServices<INotificationHandler<TNotification>>())
        {
            count++;
        }

        return count;
    }

    private static void ThrowIfFailures(Exception?[] failures, CancellationToken ct)
    {
        // Handler failures carry the handler type; resolution failures (DI factory throws, no
        // handler instance exists) are surfaced unwrapped. Both throw before the cancellation check
        // so real errors win over caller cancellation.
        var exceptions = failures.OfType<NotificationHandlerException>().ToArray();

        if (exceptions.Length > 0)
            throw new AggregateException("One or more notification handlers failed.", exceptions);

        var resolutionFailures = new List<Exception>(failures.Length);
        foreach (var failure in failures)
        {
            if (failure is not null)
            {
                resolutionFailures.Add(failure);
            }
        }

        if (resolutionFailures.Count > 0)
            throw new AggregateException("One or more notification handlers failed.", resolutionFailures);

        ct.ThrowIfCancellationRequested();
    }

    private static async Task<NotificationHandlerException?> ExecuteHandler<TNotification>(INotificationHandler<TNotification> handler, TNotification notification, CancellationToken ct) where TNotification : INotification
    {
        try
        {
            await handler.Handle(notification, ct).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            return new NotificationHandlerException(handler.GetType(), ex);
        }
    }
}

