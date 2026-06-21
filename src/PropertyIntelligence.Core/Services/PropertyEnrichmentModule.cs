using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Registry;

namespace PropertyIntelligence.Core.Services;

/// <summary>
/// Orchestrates data-source enrichment for a property address.
///
/// <para><b>Registry-driven (T086):</b> iterates ONLY the providers returned by
/// <see cref="IProviderRegistry.GetEnabled()"/> — disabled registry entries are
/// skipped BEFORE any fan-out. Per-provider timeout is resolved from the
/// registry descriptor (<see cref="ProviderDescriptor.Timeout"/>), not from a
/// hardcoded provider switch. Cache TTL is likewise registry-derived.</para>
///
/// <para><b>Skeleton (T086):</b> the typed fan-out that dispatches to concrete
/// <c>IDataProvider&lt;TResult&gt;</c> adapters and aggregates their results into a
/// <see cref="PropertyProfile"/>, plus raw-payload persistence to
/// <c>data_provider_raw_logs</c>, is implemented in <b>T026</b>. Until then,
/// <see cref="EnrichAsync"/> resolves the enabled registry set and reports the
/// registry selection so the wiring can be exercised end-to-end with mocks.</para>
/// </summary>
public sealed class PropertyEnrichmentModule
{
    private readonly IProviderRegistry _registry;
    private readonly ILogger<PropertyEnrichmentModule> _logger;

    public PropertyEnrichmentModule(
        IProviderRegistry registry,
        ILogger<PropertyEnrichmentModule> logger)
    {
        _registry = registry;
        _logger   = logger;
    }

    /// <summary>
    /// Resolves the enabled provider set from the registry and reports the
    /// selection. The typed fan-out is TODO T026.
    /// </summary>
    public Task<PropertyProfile> EnrichAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var enabled = _registry.GetEnabled();

        // Per-provider timeout is derived from the registry descriptor, not a
        // hardcoded switch. Demonstrated here by materialising a CTS per
        // enabled provider; T026 will attach each to its typed fetch.
        foreach (var descriptor in enabled)
        {
            using var perProviderCts = CancellationTokenSource
                .CreateLinkedTokenSource(ct);
            perProviderCts.CancelAfter(descriptor.Timeout);

            // TODO T026: resolve the typed IDataProvider<TResult> registered for
            // descriptor.ProviderId, call FetchAsync(address, perProviderCts.Token),
            // catch per-provider exceptions → record in ProvidersUnavailable, store
            // the raw payload in data_provider_raw_logs (provider_id, provider_version,
            // from_cache), and place the typed result into the PropertyProfile.
            _logger.LogDebug(
                "Registry selected provider {ProviderId} (timeout={Timeout}s, ttl={Ttl}s).",
                descriptor.ProviderId,
                descriptor.Timeout.TotalSeconds,
                descriptor.CacheTtl.TotalSeconds);
        }

        var profile = new PropertyProfile
        {
            Address               = address,
            ProvidersUnavailable  = enabled.Select(d => d.ProviderId).ToList(),
        };

        // AllFromCache stays false at MVP; T026 will compute it.
        return Task.FromResult(profile);
    }
}