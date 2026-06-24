using PropertyIntelligence.Core.Registry;

namespace PropertyIntelligence.Core.Interfaces;

/// <summary>
/// Declarative registry of available data sources.
/// <see cref="PropertyEnrichmentModule"/> (and other consumers) orchestrate
/// ONLY the enabled entries returned by this registry, so enabling/disabling
/// a source is configuration-driven and requires no endpoint or scoring-engine
/// rewrites.
/// </summary>
public interface IProviderRegistry
{
    /// <summary>
    /// Returns all providers currently enabled in the catalog, in catalog order.
    /// </summary>
    IReadOnlyList<ProviderDescriptor> GetEnabled();

    /// <summary>
    /// Looks up a provider by its stable <paramref name="providerId"/>,
    /// regardless of enabled state. Returns <c>null</c> if unknown.
    /// </summary>
    ProviderDescriptor? Get(string providerId);
}
