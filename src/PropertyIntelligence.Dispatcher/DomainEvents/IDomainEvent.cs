using PropertyIntelligence.Dispatcher;

namespace PropertyIntelligence.Dispatcher.DomainEvents;

/// <summary>
/// Marker for domain events published asynchronously via
/// <see cref="DomainEventChannel"/> and processed by <see cref="DomainEventProcessor"/>.
/// Extends <see cref="INotification"/> so handlers can be registered as
/// <see cref="INotificationHandler{T}"/> and dispatched uniformly.
/// Unlike synchronous notifications, domain events are queued and
/// processed in a background <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// — they do NOT block the HTTP response.
/// </summary>
public interface IDomainEvent : INotification
{
    Guid            Id         { get; }
    DateTimeOffset  OccurredAt { get; }
}
