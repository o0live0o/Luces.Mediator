namespace Luces.Mediator;

public sealed class NotificationHandlerException : Exception
{
    public NotificationHandlerException(Type handlerType, Exception innerException) : base($"Notification handler '{handlerType.FullName}' failed.", innerException)
    {
        HandlerType = handlerType;
    }

    public Type HandlerType { get; }
}
