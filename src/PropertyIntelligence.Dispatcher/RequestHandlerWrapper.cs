using Microsoft.Extensions.DependencyInjection;

namespace PropertyIntelligence.Dispatcher;

// ── Two-level hierarchy lets a single FrozenDictionary hold every
//    (TRequest, TResponse) pair without losing generic type information. ──────

public abstract class RequestHandlerBase;

public abstract class RequestHandlerBase<TResponse> : RequestHandlerBase
{
    public abstract ValueTask<TResponse> Handle(
        IRequest<TResponse> request,
        IServiceProvider    sp,
        CancellationToken   ct);
}

/// <summary>
/// Wraps a concrete <see cref="IRequestHandler{TRequest,TResponse}"/> and its
/// pipeline behaviors. Reflection is paid once at startup; hot path is
/// FrozenDictionary lookup + typed delegate chain — zero allocations.
/// </summary>
public sealed class RequestHandlerWrapper<TRequest, TResponse>
    : RequestHandlerBase<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override ValueTask<TResponse> Handle(
        IRequest<TResponse> request,
        IServiceProvider    sp,
        CancellationToken   ct)
    {
        var typed     = (TRequest)request;
        var handler   = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        var behaviors = sp.GetServices<IPipelineBehavior<TRequest, TResponse>>();

        // Build pipeline: handler is the innermost call; behaviors wrap outside-in
        // in registration order (first registered = outermost).
        RequestHandlerDelegate<TResponse> pipeline =
            () => handler.Handle(typed, ct);

        foreach (var behavior in behaviors.Reverse())
        {
            var next    = pipeline;
            var current = behavior;
            pipeline    = () => current.Handle(typed, next, ct);
        }

        return pipeline();
    }
}

// ── Notification handler base (parallel to request handler) ─────────────────

public abstract class NotificationHandlerBase
{
    public abstract ValueTask Handle(
        INotification    notification,
        IServiceProvider sp,
        CancellationToken ct);
}

public sealed class NotificationHandlerWrapper<TNotification>
    : NotificationHandlerBase
    where TNotification : INotification
{
    public override ValueTask Handle(
        INotification    notification,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var typed   = (TNotification)notification;
        var handler = sp.GetRequiredService<INotificationHandler<TNotification>>();
        return handler.Handle(typed, ct);
    }
}
