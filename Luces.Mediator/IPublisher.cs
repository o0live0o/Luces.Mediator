namespace Luces.Mediator;

public interface IPublisher
{
    Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification;

    Task PublishParallel<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification;
}
