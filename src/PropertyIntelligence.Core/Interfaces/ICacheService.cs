namespace PropertyIntelligence.Core.Interfaces;

/// <summary>
/// Generic Redis-backed cache service.
/// Key format: <c>{provider}:{SHA256(normalizedAddress.ToLowerInvariant())[..16]}</c>
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or <c>null</c> on a miss.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> with the given <paramref name="ttl"/>.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Removes the cached entry for <paramref name="key"/> (no-op if absent).</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}
