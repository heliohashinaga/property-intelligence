using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Core.Interfaces;

/// <summary>
/// Persists raw provider payload logs for auditability before analyses are stored.
/// </summary>
public interface IDataProviderRawLogStore
{
    /// <summary>
    /// Saves the provided raw logs to the backing store.
    /// </summary>
    Task SaveAsync(IReadOnlyList<DataProviderRawLog> logs, CancellationToken ct = default);
}
