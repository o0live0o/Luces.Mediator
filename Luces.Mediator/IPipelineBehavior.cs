namespace Luces.Mediator;

public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken ct);
