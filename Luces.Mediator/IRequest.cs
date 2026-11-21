namespace Luces.Mediator;

/// <summary>Request interface: types implementing it can be sent via <see cref="ISender.Send{TRequest,TResponse}"/> and produce a <typeparamref name="TResponse"/> response.</summary>
/// <typeparam name="TResponse">The response type; use <see cref="Unit"/> when there is no return value.</typeparam>
public interface IRequest<out TResponse>
{

}

/// <summary>Stream request interface: types implementing it can be sent via <see cref="ISender.CreateStream{TRequest,TResponse}"/> and produce a response stream.</summary>
/// <typeparam name="TResponse">The stream element type.</typeparam>
public interface IStreamRequest<out TResponse>
{

}

/// <summary>Represents a response type with no value.</summary>
public readonly record struct Unit
{
    /// <summary>The single Unit instance.</summary>
    public static readonly Unit Value = default;
}
