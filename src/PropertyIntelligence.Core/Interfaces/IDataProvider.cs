using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Core.Interfaces;

/// <summary>
/// Contract for all external data-source providers.
/// Each provider fetches one type of data for a given address,
/// caches the result in Redis under its own TTL, and exposes
/// the raw payload before any transformation.
/// </summary>
public interface IDataProvider<TResult>
{
    /// <summary>Unique provider identifier used as the Redis key prefix.</summary>
    string ProviderName { get; }

    /// <summary>How long the provider's result is cached in Redis.</summary>
    TimeSpan CacheTtl { get; }

    /// <summary>
    /// Fetches data for <paramref name="address"/> and returns both the typed
    /// domain payload and the original raw source payload captured before
    /// transformation, so the original is recoverable for audit.
    /// </summary>
    Task<ProviderFetchResult<TResult>> FetchAsync(PropertyAddress address, CancellationToken ct = default);
}
