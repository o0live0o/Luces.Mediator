namespace Luces.Mediator;

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> Handle(TRequest request, CancellationToken ct);
}

public interface IStreamRequestHandler<in TRequest, out TResponse> where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken ct);
}