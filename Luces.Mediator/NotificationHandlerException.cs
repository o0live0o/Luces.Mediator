namespace Luces.Mediator;

/// <summary>Indicates a notification handler execution failure and carries the failing handler's type.</summary>
public sealed class NotificationHandlerException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="handlerType">The failing handler's type.</param>
    /// <param name="innerException">The original exception thrown by the handler.</param>
    public NotificationHandlerException(Type handlerType, Exception innerException) : base($"Notification handler '{handlerType.FullName}' failed.", innerException)
    {
        HandlerType = handlerType;
    }

    /// <summary>The failing handler's type.</summary>
    public Type HandlerType { get; }
}
