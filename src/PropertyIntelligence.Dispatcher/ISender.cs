namespace PropertyIntelligence.Dispatcher;

/// <summary>Sends a command or query through the pipeline to a single handler.</summary>
public interface ISender
{
    ValueTask<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken   ct = default);
}

/// <summary>Publishes a notification to all registered handlers (in-process).</summary>
public interface IPublisher
{
    ValueTask Publish(INotification notification, CancellationToken ct = default);
}
