using Microsoft.Extensions.DependencyInjection;

namespace Luces.Mediator;

public sealed class Publisher : IPublisher
{
    private readonly IServiceProvider _serviceProvider;

    public Publisher(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

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

    public async Task PublishParallel<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        ct.ThrowIfCancellationRequested();
        var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>().ToArray();

        if (handlers.Length == 0)
            return;

        var failures = await Task.WhenAll(handlers.Select(handler => ExecuteHandler(handler, notification, ct))).ConfigureAwait(false);

        var exceptions = failures.OfType<NotificationHandlerException>().ToArray();

        if (exceptions.Length > 0)
            throw new AggregateException("One or more notification handlers failed.", exceptions);

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
        catch(Exception ex)
        {
            return new NotificationHandlerException(handler.GetType(), ex);
        }
    }
}
