namespace Luces.Mediator;

/// <summary>Handles a notification; multiple handlers may be registered for one notification.</summary>
/// <typeparam name="TNotification">The notification type to handle.</typeparam>
public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    /// <summary>Handles the notification.</summary>
    /// <param name="notification">The published notification instance.</param>
    /// <param name="ct">The caller's cancellation token; observe it cooperatively.</param>
    /// <returns>A task representing the completion of handling.</returns>
    Task Handle(TNotification notification, CancellationToken ct = default);
}
