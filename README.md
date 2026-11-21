# Luces.Mediator

[中文](README.zh-cn.md) | **English**

Luces.Mediator is a lightweight mediator library for .NET (supports netstandard2.0 and .NET 8/9/10/11), written in C# 12. The NuGet package ships both the `Luces.Mediator` core API and a Source Generator that produces `ISender` type-inference extension methods.

## Features

- **Request/handler pattern**: sync requests (`IRequest<TResponse>`) and stream requests (`IStreamRequest<TResponse>`)
- **Notification publishing**: serial (`Publish`) and concurrent (`PublishParallel`)
- **Pipeline behaviors**: cross-cutting concerns (authorization, validation, logging, ...) via `IPipelineBehavior`
- **Type safety**: the Source Generator emits strongly typed extension methods with compile-time type inference
- **Value type support**: built-in `Unit` struct for requests without a return value

## Installation

```sh
dotnet add package Luces.Mediator
```

## Development

```sh
dotnet test Luces.Mediator.slnx    # run all unit tests (core library + Source Generator)
dotnet pack Luces.Mediator/Luces.Mediator.csproj -c Release -o artifacts
```

Tests live under `tests/`: `Luces.Mediator.Tests` covers the `Sender`/`Publisher` behavioral contracts (pipeline order, cancellation semantics, exception aggregation), and `Luces.Mediator.SourceGen.Tests` drives the generator in memory via `CSharpGeneratorDriver`, covering request discovery, the MSBuild configuration matrix, and the MED001–MED007 diagnostics.

The core library targets five frameworks: `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`, and `net11.0` (nuget.org's Frameworks panel shows the full support matrix for .NET 8/9/10/11 and .NET Framework/legacy runtimes). `netstandard2.0` covers .NET Framework and older runtimes, and each `net8.0`–`net11.0` asset is selected by the nearest matching modern .NET project. `samples/ConsumerSmoke` is an end-to-end smoke project — `dotnet pack` into `samples/ConsumerSmoke/packages` first, then `dotnet run --project samples/ConsumerSmoke -f <tfm>`. It consumes a real .nupkg (not a project reference) to validate generator wiring, the buildTransitive props, TFM asset selection, and runtime behavior, running once per runtime for net8.0/net9.0/net10.0/net11.0. CI runs the same flow after packing. The repo `global.json` requires the .NET 11 SDK (which can build all the lower TFMs).

## Core interfaces

| Interface | Description |
| --- | --- |
| `IRequest<TResponse>` | Plain request interface |
| `IStreamRequest<TResponse>` | Stream request interface |
| `INotification` | Notification interface |
| `ISender` | Entry point for sending requests |
| `IPublisher` | Entry point for publishing notifications |
| `IRequestHandler<TRequest, TResponse>` | Request handler |
| `IStreamRequestHandler<TRequest, TResponse>` | Stream request handler |
| `INotificationHandler<TNotification>` | Notification handler |
| `IPipelineBehavior<TRequest, TResponse>` | Pipeline behavior |
| `PublisherOptions` | `Publisher` parallel-publish configuration (per-handler isolated child scopes) |

## Using the Source Generator

Implement `IRequest<TResponse>` for plain requests, or `IStreamRequest<TResponse>` for stream requests:

```csharp
using Luces.Mediator;

public sealed record GetGreeting(string Name) : IRequest<string>;

public sealed record WatchUpdates(string Topic) : IStreamRequest<Update>;

public sealed record Update(string Message);

// Requests without a return value use Unit
public sealed record LogRequest(string Message) : IRequest<Unit>;
```

After installing `Luces.Mediator`, the Source Generator emits `ISender` extension methods for these requests. The generated namespace defaults to the `ISender` namespace (`Luces.Mediator`), so you can call them directly:

```csharp
string greeting = await sender.Send(new GetGreeting("Ada"), cancellationToken);

await foreach (var update in sender.CreateStream(new WatchUpdates("news"), cancellationToken))
{
    Console.WriteLine(update.Message);
}
```

The generated extension methods infer the response type from the request type and ultimately call `ISender.Send<TRequest, TResponse>` or `ISender.CreateStream<TRequest, TResponse>`. Creating handlers and `ISender`, plus DI registration, are the application's responsibility.

## Configuring the Source Generator

Configure the generator in your app's `.csproj`. The generator is enabled by default; options not set fall back to the table's defaults:

```xml
<PropertyGroup>
  <LucesMediatorExtensionsEnabled>true</LucesMediatorExtensionsEnabled>
  <LucesMediatorExtensionsNamespace>MyApp.Generated</LucesMediatorExtensionsNamespace>
  <LucesMediatorExtensionsClassName>SenderExtensions</LucesMediatorExtensionsClassName>
  <LucesMediatorExtensionsSendMethod>Send</LucesMediatorExtensionsSendMethod>
  <LucesMediatorExtensionsStreamMethod>CreateStream</LucesMediatorExtensionsStreamMethod>
</PropertyGroup>
```

After configuring a custom namespace, source files calling the extension methods must import it:

```csharp
using MyApp.Generated;
```

Supported MSBuild properties:

| Property | Default | Description |
| --- | --- | --- |
| `LucesMediatorExtensionsEnabled` | `true` | Enables the generator. Set to `false` to turn generation off. |
| `LucesMediatorExtensionsSenderType` | `Luces.Mediator.ISender` | Fully qualified name of the sender interface to find and extend. |
| `LucesMediatorExtensionsRequestType` | `Luces.Mediator.IRequest` | Fully qualified request interface name; it must have one type parameter. |
| `LucesMediatorExtensionsStreamTypeName` | `Luces.Mediator.IStreamRequest` | Fully qualified stream request interface name. |
| `LucesMediatorExtensionsNamespace` | The sender interface's namespace | Namespace for generated code. |
| `LucesMediatorExtensionsClassName` | `SenderExtensions` | Name of the generated static extension class. |
| `LucesMediatorExtensionsSendMethod` | `Send` | Generated method name for plain requests. |
| `LucesMediatorExtensionsStreamMethod` | `CreateStream` | Generated method name for stream requests. |

These properties can live in the app project's `<PropertyGroup>`; for multiple projects under the same directory, put them in `Directory.Build.props`. When changing `SenderType`, `RequestType`, or `StreamTypeName`, make sure the interface exists in the compilation target and its full name matches the configuration.

## Behavioral contracts

### Compilation scope of request discovery

The generator only emits extension methods for request types in the **current compilation**; requests defined in referenced assemblies (e.g. a separate Contracts project) are not discovered automatically. If request types live in a separate project, either enable the same generator in the project defining them (the generated extension methods share the `ISender` namespace by default, are directly referenceable, and do not produce duplicate overloads from visibility) or use explicit generic calls at the call site:

```csharp
string greeting = await sender.Send<GetGreeting, string>(new GetGreeting("Ada"), cancellationToken);
```

Requests nested in generic outer types, `private` and `file`-local types, and `abstract` types are skipped; no extension methods are generated for them.

### Cancellation semantics of notification publishing

- `PublishParallel` checks the caller's token before starting and after all handlers complete.
- `Publish`, being serial, checks the caller's token at entry, before scheduling each handler, and after all handlers complete: a pre-cancelled token resolves and executes no handlers, cancellation stops scheduling further handlers, and if the last handler cancels the token but returns normally, an OperationCanceledException is still thrown. This is a behavior change from earlier versions (calls that previously returned successfully now throw `OperationCanceledException` when the token is cancelled).
- These checks are best-effort scheduling-boundary checks and **never force-terminate already-started handlers**; started handlers must still observe the token cooperatively. The publisher does not wrap handlers in `Task.Run` to fake forced cancellation.
- `Send` and stream requests pass the token through unchanged; the handler side observes it cooperatively.

### Nulls, exceptions, and parallel dependencies

- The `Sender` and `Publisher` constructors use `ArgumentNullException.ThrowIfNull` for `IServiceProvider`, surfacing mistakes at construction.
- `Send`, `CreateStream`, `Publish`, and `PublishParallel` reject `null` requests/notifications at entry with `ArgumentNullException`; value-type requests/notifications are unaffected.
- `Send` resolves the handler lazily: the handler is constructed inside the terminal pipeline delegate, so short-circuiting behaviors (e.g. authorization/permission pipelines) never trigger handler construction and expensive dependencies are not built for nothing. When a behavior short-circuits, the handler is never constructed; with no pipeline behaviors, a resolution failure (zero or multiple registrations) still throws synchronously at the call site, and with behaviors it surfaces when the pipeline reaches the terminal.
- By default `PublishParallel` reuses the caller's single DI scope: non-thread-safe scoped dependencies (e.g. one EF Core `DbContext`) may be used concurrently, in which case switch to serial `Publish` or isolate at the business layer. Parallelism only overlaps the asynchronous parts inside handlers; long synchronous work still delays other handlers' startup.
- Passing `new PublisherOptions { IsolateParallelHandlerScopes = true }` to the `Publisher` constructor gives each handler its own child scope (`CreateAsyncScope`), so scoped dependencies are no longer shared across handlers and are disposed when each handler completes. This mode requires `IServiceScopeFactory` to be registered in the container (`BuildServiceProvider` provides it by default); singleton handler instances are still shared across handlers (isolation protects scoped/transient dependencies only); handler activations increase (once in the counting phase plus once in each handler's child scope, except for singletons).
- Handler exceptions are aggregated into a `NotificationHandlerException` carrying `HandlerType` and thrown by `PublishParallel` as an `AggregateException`; when caller cancellation and a real error happen together, the real error wins; exceptions from handler resolution (the DI factory) cannot be attributed to a handler instance, so they are not wrapped as `NotificationHandlerException` — the non-isolated path throws the raw exception synchronously at entry, and the isolated path throws it during the counting phase.

## Example: request handler

```csharp
// Define the request
public sealed record GetUser(int Id) : IRequest<User?>;

public sealed record User(int Id, string Name);

// Define the handler
public sealed class GetUserHandler : IRequestHandler<GetUser, User?>
{
    private readonly IUserRepository _repository;

    public GetUserHandler(IUserRepository repository) => _repository = repository;

    public async Task<User?> Handle(GetUser request, CancellationToken cancellationToken)
    {
        return await _repository.GetByIdAsync(request.Id, cancellationToken);
    }
}
```

## Example: pipeline behavior

```csharp
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        var response = await next(cancellationToken);
        Console.WriteLine($"Handled {typeof(TRequest).Name}");
        return response;
    }
}
```

## DI registration example

```csharp
// Register in ASP.NET Core or any other DI container
services.AddSingleton<ISender>(sp => new Sender(sp));
services.AddSingleton<IPublisher>(sp => new Publisher(sp));

// Optional: give each parallel handler its own child scope so scoped dependencies
// are never shared concurrently
// services.AddSingleton<IPublisher>(sp => new Publisher(sp, new PublisherOptions { IsolateParallelHandlerScopes = true }));

// Register handlers
services.AddTransient<IRequestHandler<GetGreeting, string>, GetGreetingHandler>();

// Register pipeline behaviors (executed in registration order)
services.AddTransient<IPipelineBehavior<GetGreeting, string>, LoggingBehavior<GetGreeting, string>>();
```
