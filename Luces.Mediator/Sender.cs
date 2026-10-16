using Microsoft.Extensions.DependencyInjection;

namespace Luces.Mediator;

public sealed class Sender : ISender
{
    private readonly IServiceProvider _serviceProvider;

    public Sender(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }
    public IAsyncEnumerable<TResponse> CreateStream<TRequest, TResponse>(TRequest request, CancellationToken ct = default) where TRequest : IStreamRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = GetHandler<IStreamRequestHandler<TRequest, TResponse>>();
        return handler.Handle(request, ct);
    }

    public ValueTask<TResponse> Send<TRequest, TResponse>(TRequest request, CancellationToken ct = default) where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(request);

        var handler = GetHandler<IRequestHandler<TRequest, TResponse>>();
        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();

        RequestHandlerDelegate<TResponse> next = c => handler.Handle(request, c);

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
