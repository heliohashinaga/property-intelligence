using System.Security.Cryptography;
using System.Text;

namespace PropertyIntelligence.Providers.Shared;

/// <summary>
/// Builds Redis cache keys in the format <c>{providerName}:{SHA256(normalizedAddress)[..16]}</c>.
/// </summary>
public static class CacheKeyHelper
{
    /// <summary>
    /// Returns a stable cache key for the given provider and normalized address.
    /// </summary>
    /// <param name="providerName">Provider identifier, e.g. "viacep".</param>
    /// <param name="normalizedAddress">Normalized address string (case-insensitive).</param>
    public static string BuildKey(string providerName, string normalizedAddress)
    {
        var input = normalizedAddress.ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{providerName}:{hex[..16]}";
    }
}
