namespace PropertyIntelligence.Dispatcher;

/// <summary>
/// In-process CQRS dispatcher. Zero reflection on the hot path.
/// Registered as Scoped so it inherits the request DI scope.
/// </summary>
public sealed class Dispatcher(IServiceProvider sp, DispatcherRegistry registry)
    : ISender, IPublisher
{
    /// <inheritdoc/>
    public ValueTask<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken   ct = default)
    {
        var type = request.GetType();
        if (!registry.Requests.TryGetValue(type, out var wrapper))
            throw new InvalidOperationException(
                $"No handler registered for request type '{type.FullName}'.");

        return ((RequestHandlerBase<TResponse>)wrapper).Handle(request, sp, ct);
    }

    /// <inheritdoc/>
    public async ValueTask Publish(INotification notification, CancellationToken ct = default)
    {
        if (!registry.Notifications.TryGetValue(notification.GetType(), out var handlers))
            return;

        foreach (var h in handlers)
            await h.Handle(notification, sp, ct);
    }
}
