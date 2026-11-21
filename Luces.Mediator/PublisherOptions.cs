namespace Luces.Mediator;

/// <summary>
/// Notification publishing options for <see cref="Publisher"/>.
/// </summary>
public sealed class PublisherOptions
{
    /// <summary>
    /// When <see langword="true"/>, <see cref="IPublisher.PublishParallel"/> resolves each handler inside its own
    /// child DI scope, so scoped dependencies (e.g. an EF Core <c>DbContext</c>) are never shared concurrently,
    /// and child scopes are disposed when the handler completes. When <see langword="false"/> (default), all
    /// handlers share the caller's scope, and non-thread-safe scoped dependencies must not be used concurrently.
    /// </summary>
    public bool IsolateParallelHandlerScopes { get; init; }
}
