namespace PropertyIntelligence.Dispatcher;

/// <summary>Marker interface for in-process notifications (fan-out to N handlers).</summary>
public interface INotification;

/// <summary>Handles a specific notification type.</summary>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    ValueTask Handle(TNotification notification, CancellationToken ct);
}
