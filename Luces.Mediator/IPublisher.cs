namespace Luces.Mediator;

/// <summary>Publishes notifications.</summary>
public interface IPublisher
{
    /// <summary>Publishes the notification to handlers serially in DI registration order; the first handler exception stops scheduling further handlers.</summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="ct">The cancellation token, checked at entry, before scheduling each handler, and after all handlers complete.</param>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <returns>A task representing the completion of all handlers.</returns>
    Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification;

    /// <summary>Publishes the notification to all handlers concurrently; handler failures are aggregated into an <see cref="AggregateException"/> containing <see cref="NotificationHandlerException"/> instances.</summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="ct">The cancellation token, checked at entry and after all handlers complete; started handlers must observe it cooperatively.</param>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <returns>A task representing the completion of all handlers.</returns>
    Task PublishParallel<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification;
}
