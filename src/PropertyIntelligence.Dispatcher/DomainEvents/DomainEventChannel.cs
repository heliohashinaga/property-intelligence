using System.Threading.Channels;

namespace PropertyIntelligence.Dispatcher.DomainEvents;

/// <summary>
/// Thread-safe unbounded channel for async domain event delivery.
/// Single-reader (the <see cref="DomainEventProcessor"/> BackgroundService).
/// Inject as singleton; write from request handlers, read from background.
/// </summary>
public sealed class DomainEventChannel
{
    private readonly Channel<IDomainEvent> _channel =
        Channel.CreateUnbounded<IDomainEvent>(
            new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<IDomainEvent> Reader => _channel.Reader;

    /// <summary>
    /// Queues an event for background processing without blocking the caller.
    /// Never awaits completion of handlers — fire and forget at the channel boundary.
    /// </summary>
    public ValueTask PublishAsync(IDomainEvent evt, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(evt, ct);
}
