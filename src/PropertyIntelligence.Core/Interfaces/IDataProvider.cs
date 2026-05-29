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
    /// Fetches data for <paramref name="address"/>.
    /// MUST store the raw HTTP/file payload via <see cref="ICacheService"/>
    /// BEFORE returning a transformed result, so the original is recoverable.
    /// </summary>
    Task<TResult> FetchAsync(PropertyAddress address, CancellationToken ct = default);
}
