namespace Luces.Mediator;

/// <summary>Handles a request and returns a response.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request.</summary>
    /// <param name="request">The sent request.</param>
    /// <param name="ct">The cancellation token; observe it cooperatively.</param>
    /// <returns>The response to the request.</returns>
    ValueTask<TResponse> Handle(TRequest request, CancellationToken ct);
}

/// <summary>Handles a stream request and returns a response stream.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The stream element type.</typeparam>
public interface IStreamRequestHandler<in TRequest, out TResponse> where TRequest : IStreamRequest<TResponse>
{
    /// <summary>Handles the request and produces the response stream; observe the cancellation token cooperatively during enumeration.</summary>
    /// <param name="request">The sent request.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The stream of response elements.</returns>
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken ct);
}
