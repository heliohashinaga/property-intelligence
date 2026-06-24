namespace PropertyIntelligence.Core.Registry;

/// <summary>
/// How a data source obtains its data.
/// </summary>
public enum SourceType
{
    /// <summary>Live external API (e.g. ViaCEP, OpenRouter).</summary>
    RealTime,

    /// <summary>Locally-imported dataset queried from the DB (e.g. ANA shapefile, IBGE CNEFE).</summary>
    Imported,

    /// <summary>Locally-stored dataset (e.g. SSP-SP CSV loaded into the DB).</summary>
    LocalDb,
}

/// <summary>
/// Declarative metadata for a single data source, mirroring the
/// <c>provider_catalog</c> table (see data-model.md §provider_catalog).
/// </summary>
/// <remarks>
/// Lives in the Core project (not Providers) so that Core contracts such as
/// <see cref="PropertyIntelligence.Core.Interfaces.IProviderRegistry"/> and
/// the Core-resident <c>PropertyEnrichmentModule</c> can reference it without
/// a circular dependency on <c>PropertyIntelligence.Providers</c>.
/// </remarks>
public sealed record ProviderDescriptor
{
    /// <summary>Stable identifier, e.g. <c>viacep</c>, <c>mock_security</c>.</summary>
    public required string ProviderId { get; init; }

    /// <summary>Human-friendly source name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Whether the source participates in enrichment fan-out.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Supported scoring dimensions / capabilities.</summary>
    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>Runtime cache TTL, resolved from <c>cache_ttl_seconds</c>.</summary>
    public required TimeSpan CacheTtl { get; init; }

    /// <summary>Per-provider timeout budget, resolved from <c>timeout_seconds</c>.</summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>How the source obtains its data.</summary>
    public required SourceType SourceType { get; init; }

    /// <summary>Adapter/source version for auditability.</summary>
    public required string Version { get; init; }
}
