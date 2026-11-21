// End-to-end smoke test: exercises the shipped .nupkg exactly like a real consumer would —
// Source Generator extensions, pipeline behaviors, and isolated-scope parallel publishing.
using Luces.Mediator;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddSingleton<ISender>(sp => new Sender(sp));
services.AddSingleton<IPublisher>(sp => new Publisher(sp, new PublisherOptions { IsolateParallelHandlerScopes = true }));
services.AddTransient<IRequestHandler<Greeting, string>, GreetingHandler>();
services.AddTransient<IPipelineBehavior<Greeting, string>, LoggingBehavior<Greeting, string>>();
services.AddScoped<ScopedCounter>();
services.AddTransient<INotificationHandler<OrderPlaced>, OrderEmailHandler>();
services.AddTransient<INotificationHandler<OrderPlaced>, OrderAuditHandler>();

await using var provider = services.BuildServiceProvider();
var sender = provider.GetRequiredService<ISender>();
var publisher = provider.GetRequiredService<IPublisher>();

// 1. Generated extension method resolves the response type at compile time.
string reply = await sender.Send(new Greeting("Luces"));
if (reply != "hello: Luces")
{
    return Fail($"unexpected Send response: {reply}");
}
Console.WriteLine($"Send: {reply}");

// 2. Pipeline behavior wrapped the handler call.
if (LoggingBehavior<Greeting, string>.Calls != 1)
{
    return Fail($"behavior ran {LoggingBehavior<Greeting, string>.Calls} times, expected 1");
}
Console.WriteLine("Behavior: ok");

// 3. Parallel publish dispatches to every handler, each with its own scoped dependency.
// The counting scope activates both handlers once (sharing one ScopedCounter), then each
// task's child scope yields its own instance: 1 + 2 = 3 total, and the two task-scope
// instances must differ from each other.
await publisher.PublishParallel(new OrderPlaced(42));
var seen = ScopedCounter.Instances;
if (OrderEmailHandler.Received != 1 || OrderAuditHandler.Received != 1)
{
    return Fail($"handlers ran email={OrderEmailHandler.Received} audit={OrderAuditHandler.Received}");
}
if (seen.Count != 3)
{
    return Fail($"expected 3 scoped instances (1 counting + 2 isolated), saw {seen.Count}");
}
if (ReferenceEquals(seen[1], seen[2]))
{
    return Fail("the two handler scopes resolved the same scoped instance; isolation broken");
}
Console.WriteLine("PublishParallel isolated scopes: ok");

Console.WriteLine("SMOKE OK");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"SMOKE FAILED: {message}");
    return 1;
}

public sealed record Greeting(string Name) : IRequest<string>;

public sealed class GreetingHandler : IRequestHandler<Greeting, string>
{
    public ValueTask<string> Handle(Greeting request, CancellationToken ct)
        => ValueTask.FromResult($"hello: {request.Name}");
}

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public static int Calls;

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return await next(ct);
    }
}

public sealed record OrderPlaced(int OrderId) : INotification;

public sealed class OrderEmailHandler : INotificationHandler<OrderPlaced>
{
    public static int Received;

    private readonly ScopedCounter _counter;

    public OrderEmailHandler(ScopedCounter counter) => _counter = counter;

    public Task Handle(OrderPlaced notification, CancellationToken ct)
    {
        Interlocked.Increment(ref Received);
        _counter.Touch();
        return Task.CompletedTask;
    }
}

public sealed class OrderAuditHandler : INotificationHandler<OrderPlaced>
{
    public static int Received;

    private readonly ScopedCounter _counter;

    public OrderAuditHandler(ScopedCounter counter) => _counter = counter;

    public Task Handle(OrderPlaced notification, CancellationToken ct)
    {
        Interlocked.Increment(ref Received);
        _counter.Touch();
        return Task.CompletedTask;
    }
}

public sealed class ScopedCounter
{
    public static readonly List<ScopedCounter> Instances = new();

    public ScopedCounter()
    {
        lock (Instances)
        {
            Instances.Add(this);
        }
    }

    public void Touch() { }
}
