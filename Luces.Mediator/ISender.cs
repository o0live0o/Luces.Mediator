namespace Luces.Mediator;

/// <summary>The entry point for sending requests.</summary>
public interface ISender
{
    /// <summary>Sends the request and returns the response; the handler is resolved as exactly one registration — zero or multiple registrations throw <see cref="InvalidOperationException"/>.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="ct">The cancellation token, passed through to behaviors and the handler.</param>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>The response to the request.</returns>
    ValueTask<TResponse> Send<TRequest, TResponse>(TRequest request, CancellationToken ct = default) where TRequest : IRequest<TResponse>;

    /// <summary>Sends the stream request and returns the response stream.</summary>
    /// <param name="request">The stream request to send.</param>
    /// <param name="ct">The cancellation token, passed through to the handler.</param>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The stream element type.</typeparam>
    /// <returns>The stream of response elements.</returns>
    IAsyncEnumerable<TResponse> CreateStream<TRequest, TResponse>(TRequest request, CancellationToken ct = default) where TRequest : IStreamRequest<TResponse>;
}
