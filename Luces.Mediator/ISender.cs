namespace Luces.Mediator;

public interface ISender
{
    ValueTask<TResponse> Send<TRequest, TResponse>(TRequest request, CancellationToken ct = default) where TRequest : IRequest<TResponse>;

    IAsyncEnumerable<TResponse> CreateStream<TRequest, TResponse>(TRequest request, CancellationToken ct = default) where TRequest : IStreamRequest<TResponse>;
}
