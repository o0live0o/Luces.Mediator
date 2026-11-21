namespace Luces.Mediator;

/// <summary>A request pipeline behavior wrapping the handler call, composed outer-to-inner in DI registration order.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Runs the behavior; invoke <paramref name="next"/> to continue the pipeline, or return without invoking to short-circuit.</summary>
    /// <param name="request">The current request.</param>
    /// <param name="next">The next delegate in the pipeline (ultimately the handler call).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The pipeline's response.</returns>
    ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

/// <summary>The terminal pipeline delegate; behaviors invoke it to hand control to the next stage.</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="ct">The cancellation token.</param>
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken ct);
