using Microsoft.Extensions.DependencyInjection;

namespace Luces.Mediator;

/// <summary>
/// The default <see cref="ISender"/> implementation; the handler is resolved lazily at the
/// pipeline terminal (exactly one registration), with behaviors composed outer-to-inner
/// in DI registration order.
/// </summary>
public sealed class Sender : ISender
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Creates the sender.</summary>
    /// <param name="serviceProvider">The service provider used to resolve handlers and behaviors; may be a scoped or root provider.</param>
    public Sender(IServiceProvider serviceProvider)
    {
        Guard.NotNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <summary>Sends the stream request and returns the response stream; the handler is resolved as exactly one registration (no pipeline behaviors).</summary>
    public IAsyncEnumerable<TResponse> CreateStream<TRequest, TResponse>(TRequest request, CancellationToken ct = default) where TRequest : IStreamRequest<TResponse>
    {
        Guard.NotNull(request);

        var handler = GetHandler<IStreamRequestHandler<TRequest, TResponse>>();
        return handler.Handle(request, ct);
    }

    /// <summary>Sends the request and returns the response; the handler is resolved lazily at the pipeline terminal, with behaviors composed outer-to-inner in DI registration order.</summary>
    public ValueTask<TResponse> Send<TRequest, TResponse>(TRequest request, CancellationToken ct = default) where TRequest : IRequest<TResponse>
    {
        Guard.NotNull(request);

        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();

        // The handler is resolved inside the terminal delegate: behaviors that short-circuit never
        // trigger handler construction, so expensive dependencies are not built for nothing.
        RequestHandlerDelegate<TResponse> next = c =>
        {
            var handler = GetHandler<IRequestHandler<TRequest, TResponse>>();
            return handler.Handle(request, c);
        };

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var inner = next;
            next = c => behavior.Handle(request, inner, c);
        }

        return next(ct);
    }

    private THandler GetHandler<THandler>() where THandler : class
    {
        var handlers = _serviceProvider.GetServices<THandler>().ToArray();
        return handlers.Length switch
        {
            1 => handlers[0],
            0 => throw new InvalidOperationException($"No handler is registered for '{typeof(THandler).FullName}'."),
            _ => throw new InvalidOperationException($"Multiple handlers are registered for '{typeof(THandler).FullName}'.")
        };
    }
}

