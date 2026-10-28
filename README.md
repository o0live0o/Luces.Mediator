# Luces.Mediator

Luces.Mediator 是面向 .NET 10 的轻量级 mediator 库，采用 C# 12 编写。NuGet 包同时包含 `Luces.Mediator` 核心 API 和用于生成 `ISender` 类型推断扩展方法的 Source Generator。

## 功能特性

- **请求-处理器模式**：支持同步请求 (`IRequest<TResponse>`) 和流式请求 (`IStreamRequest<TResponse>`)
- **通知发布**：支持串行发布 (`Publish`) 和并行发布 (`PublishParallel`)
- **管道行为**：通过 `IPipelineBehavior` 实现请求处理管道，支持授权、验证、日志等横切关注点
- **类型安全**：Source Generator 自动生成强类型的扩展方法，提供编译时类型推断
- **值类型支持**：内置 `Unit` 结构用于无返回值的请求

## 安装

```sh
dotnet add package Luces.Mediator
```

## 核心接口

| 接口 | 说明 |
| --- | --- |
| `IRequest<TResponse>` | 普通请求接口 |
| `IStreamRequest<TResponse>` | 流式请求接口 |
| `INotification` | 通知接口 |
| `ISender` | 发送请求的入口 |
| `IPublisher` | 发布通知的入口 |
| `IRequestHandler<TRequest, TResponse>` | 请求处理器 |
| `IStreamRequestHandler<TRequest, TResponse>` | 流式请求处理器 |
| `INotificationHandler<TNotification>` | 通知处理器 |
| `IPipelineBehavior<TRequest, TResponse>` | 管道行为 |

## 使用 Source Generator

定义请求时实现 `IRequest<TResponse>`，或为流式请求实现 `IStreamRequest<TResponse>`：

```csharp
using Luces.Mediator;

public sealed record GetGreeting(string Name) : IRequest<string>;

public sealed record WatchUpdates(string Topic) : IStreamRequest<Update>;

public sealed record Update(string Message);

// 无返回值的请求使用 Unit
public sealed record LogRequest(string Message) : IRequest<Unit>;
```

安装 `Luces.Mediator` 后，Source Generator 会为这些请求生成 `ISender` 扩展方法。默认生成命名空间与 `ISender` 相同（`Luces.Mediator`），因此可以直接调用：

```csharp
string greeting = await sender.Send(new GetGreeting("Ada"), cancellationToken);

await foreach (var update in sender.CreateStream(new WatchUpdates("news"), cancellationToken))
{
    Console.WriteLine(update.Message);
}
```

生成的扩展方法会根据请求类型推断响应类型，底层仍调用 `ISender.Send<TRequest, TResponse>` 或 `ISender.CreateStream<TRequest, TResponse>`。请求处理器和 `ISender` 的创建、依赖注入注册由应用自行配置。

## 配置 Source Generator

可以在应用的 `.csproj` 中配置生成器。默认情况下生成器已启用，未指定的选项使用表格中的默认值：

```xml
<PropertyGroup>
  <LucesMediatorExtensionsEnabled>true</LucesMediatorExtensionsEnabled>
  <LucesMediatorExtensionsNamespace>MyApp.Generated</LucesMediatorExtensionsNamespace>
  <LucesMediatorExtensionsClassName>SenderExtensions</LucesMediatorExtensionsClassName>
  <LucesMediatorExtensionsSendMethod>Send</LucesMediatorExtensionsSendMethod>
  <LucesMediatorExtensionsStreamMethod>CreateStream</LucesMediatorExtensionsStreamMethod>
</PropertyGroup>
```

配置自定义生成命名空间后，调用扩展方法的源文件需要导入该命名空间：

```csharp
using MyApp.Generated;
```

支持的 MSBuild 属性如下：

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `LucesMediatorExtensionsEnabled` | `true` | 是否启用生成器。设置为 `false` 可关闭生成。 |
| `LucesMediatorExtensionsSenderType` | `Luces.Mediator.ISender` | 要查找并扩展的 sender 接口全名。 |
| `LucesMediatorExtensionsRequestType` | `Luces.Mediator.IRequest` | 请求接口全名，接口应有一个类型参数。 |
| `LucesMediatorExtensionsStreamTypeName` | `Luces.Mediator.IStreamRequest` | 流式请求接口全名，接口应有一个类型参数。 |
| `LucesMediatorExtensionsNamespace` | sender 接口所在的命名空间 | 生成代码的命名空间。 |
| `LucesMediatorExtensionsClassName` | `SenderExtensions` | 生成的静态扩展类名。 |
| `LucesMediatorExtensionsSendMethod` | `Send` | 普通请求生成的扩展方法名。 |
| `LucesMediatorExtensionsStreamMethod` | `CreateStream` | 流式请求生成的扩展方法名。 |

可将这些属性放在应用项目的 `<PropertyGroup>` 中；若要对同一目录下多个项目生效，也可以放入 `Directory.Build.props`。修改 `SenderType`、`RequestType` 或 `StreamTypeName` 时，请确保对应接口存在于编译目标中，并且接口全名与配置一致。

## 行为契约

### 请求发现的编译范围

生成器只为**当前编译**中的请求类型生成扩展方法；引用程序集（例如单独的 Contracts 项目）中定义的请求不会被自动发现。如果请求类型位于单独的项目，请在定义它们的项目中启用同一生成器（生成的扩展方法默认与 `ISender` 同命名空间并可直接引用，且不会因可见性产生重复重载），或者在调用方改用显式泛型调用：

```csharp
string greeting = await sender.Send<GetGreeting, string>(new GetGreeting("Ada"), cancellationToken);
```

泛型外部类型中的嵌套请求、`private`、`file` 局部以及 `abstract` 类型都会被跳过，不会生成扩展方法。

### 通知发布的取消语义

- `PublishParallel` 在开始前与全部处理器完成后都会检查调用方令牌。
- `Publish` 作为串行发布，在入口、每次调度处理器之前以及全部处理器完成后都会检查调用方令牌：预取消的令牌不会解析或执行任何处理器，取消后不再调度后续处理器，最后一个处理器取消令牌但正常返回时也会抛出取消异常。这是相对早期版本的行为变化（成功返回的调用在令牌已取消时会改抛 `OperationCanceledException`）。
- 该检查是尽力而为的调度边界检查，**不承诺强制终止已经开始执行的处理器**；已启动的处理器仍需按协作式约定自行观察令牌，发布器不会用 `Task.Run` 包装来模拟强制取消。
- `Send` 与流式请求保持透传令牌的语义，由处理器侧按协作式约定处理。

### 空值、异常与并行依赖

- `Sender` 与 `Publisher` 的构造函数对 `IServiceProvider` 使用 `ArgumentNullException.ThrowIfNull`，错误会在构造入口立即暴露。
- `Send`、`CreateStream`、`Publish`、`PublishParallel` 在入口拒绝 `null` 请求或通知并抛出 `ArgumentNullException`；值类型请求/通知不受影响。
- `Send` 会在执行管道之前构造处理器，即使是会短路的授权/权限管道行为，处理器也会被构造（影响昂贵依赖与构造异常）。
- `PublishParallel` 的所有处理器复用同一个 DI scope；非线程安全的 scoped 依赖（例如同一个 EF Core `DbContext`）可能被并发使用，此时应改用串行 `Publish` 或在业务层自行隔离。并行只并发处理器内部的异步部分，长时间的同步工作仍会阻塞其他处理器启动。
- 处理器异常会被聚合为带 `HandlerType` 的 `NotificationHandlerException` 并由 `PublishParallel` 抛出为 `AggregateException`；调用方取消与真实错误同时发生时以真实错误优先；处理器解析（DI 工厂）阶段的异常不会包装进 `NotificationHandlerException`。

## 示例：请求处理器

```csharp
// 定义请求
public sealed record GetUser(int Id) : IRequest<User?>;

public sealed record User(int Id, string Name);

// 定义处理器
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

## 示例：管道行为

```csharp
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResponse> next)
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        var response = await next(cancellationToken);
        Console.WriteLine($"Handled {typeof(TRequest).Name}");
        return response;
    }
}
```

## 依赖注入注册示例

```csharp
// 在 ASP.NET Core 或其他 DI 容器中注册
services.AddSingleton<ISender>(sp => new Sender(sp));
services.AddSingleton<IPublisher>(sp => new Publisher(sp));

// 注册处理器
services.AddTransient<IRequestHandler<GetGreeting, string>, GetGreetingHandler>();

// 注册管道行为（按顺序执行）
services.AddTransient<IPipelineBehavior<GetGreeting, string>, LoggingBehavior<GetGreeting, string>>();
```