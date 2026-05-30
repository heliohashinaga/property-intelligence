namespace PropertyIntelligence.Core.Interfaces;

/// <summary>
/// Persistence port for raw provider payloads.
/// Constitution §II: raw payloads stored before transformation so the original is always recoverable.
/// </summary>
public interface IRawLogRepository
{
    Task LogAsync(
        Guid addressId,
        string providerName,
        string rawPayload,
        CancellationToken ct = default);
}
