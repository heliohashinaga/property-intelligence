using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Core.Interfaces;

/// <summary>
/// Normalizes a raw free-text address into a structured <see cref="PropertyAddress"/>.
/// Returns null when the address is unrecognizable or too ambiguous.
/// </summary>
public interface IAddressNormalizer
{
    /// <summary>
    /// Attempts to normalize <paramref name="rawAddress"/>.
    /// </summary>
    /// <returns>
    /// A normalized <see cref="PropertyAddress"/>, or <c>null</c> when the address
    /// cannot be resolved — the caller should return HTTP 422 in that case.
    /// </returns>
    Task<PropertyAddress?> NormalizeAsync(string rawAddress, CancellationToken ct = default);
}
