namespace Luces.Mediator;

public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken ct = default);
}
