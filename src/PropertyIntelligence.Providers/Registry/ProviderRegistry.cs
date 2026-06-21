using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Registry;

namespace PropertyIntelligence.Providers.Registry;

/// <summary>
/// In-memory implementation of <see cref="IProviderRegistry"/>.
/// Catalog entries are injected at startup (bound from configuration / seeded
/// from <c>provider_catalog</c>) and cached for the lifetime of the singleton.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly IReadOnlyList<ProviderDescriptor> _all;
    private readonly IReadOnlyList<ProviderDescriptor> _enabled;
    private readonly Dictionary<string, ProviderDescriptor> _byId;

    public ProviderRegistry(IEnumerable<ProviderDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _all = descriptors.ToList();
        _enabled = _all.Where(d => d.Enabled).ToList();
        _byId = _all.ToDictionary(d => d.ProviderId, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public IReadOnlyList<ProviderDescriptor> GetEnabled() => _enabled;

    /// <inheritdoc />
    public ProviderDescriptor? Get(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _byId.TryGetValue(providerId, out var d) ? d : null;
    }
}