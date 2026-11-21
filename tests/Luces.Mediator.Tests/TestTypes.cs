using Luces.Mediator;

namespace Luces.Mediator.Tests;

public sealed record Ping(string Name) : IRequest<string>;
public sealed record Pong : IRequest<Unit>;
public sealed record StreamQuery(int Count) : IStreamRequest<int>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult($"pong: {request.Name}");
}

public sealed class TokenObservingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> Handle(Ping request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult("pong");
    }
}

public sealed class UnitHandler : IRequestHandler<Pong, Unit>
{
    public ValueTask<Unit> Handle(Pong request, CancellationToken ct) => ValueTask.FromResult(Unit.Value);
}

public sealed class StreamQueryHandler : IStreamRequestHandler<StreamQuery, int>
{
    public async IAsyncEnumerable<int> Handle(StreamQuery request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < request.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed class RecordingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public List<string> Log { get; } = new();

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        Log.Add($"before:{typeof(TRequest).Name}");
        var response = await next(ct);
        Log.Add($"after:{typeof(TRequest).Name}");
        return response;
    }
}

public sealed class ShortCircuitBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly TResponse _response;

    public ShortCircuitBehavior(TResponse response) => _response = response;

    public ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // Never calls next: models an authorization behavior that short-circuits.
        return ValueTask.FromResult(_response);
    }
}

public sealed class ThrowingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        throw new InvalidOperationException("pipeline blew up");
    }
}
