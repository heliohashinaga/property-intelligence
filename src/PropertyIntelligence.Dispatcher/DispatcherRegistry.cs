using System.Collections.Frozen;

namespace PropertyIntelligence.Dispatcher;

/// <summary>
/// Immutable lookup tables built once at startup.
/// FrozenDictionary gives O(1) read with no locking overhead.
/// </summary>
public sealed class DispatcherRegistry(
    FrozenDictionary<Type, RequestHandlerBase>               requests,
    IReadOnlyDictionary<Type, IReadOnlyList<NotificationHandlerBase>> notifications)
{
    public FrozenDictionary<Type, RequestHandlerBase>                Requests      { get; } = requests;
    public IReadOnlyDictionary<Type, IReadOnlyList<NotificationHandlerBase>> Notifications { get; } = notifications;
}
