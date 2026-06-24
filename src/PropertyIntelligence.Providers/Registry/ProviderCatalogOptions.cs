namespace PropertyIntelligence.Providers.Registry;

/// <summary>
/// Binds the <c>Providers:Catalog</c> configuration array to a list of
/// <see cref="ProviderCatalogEntry"/> entries, mirroring the
/// <c>provider_catalog</c> table (see data-model.md §provider_catalog).
/// </summary>
public sealed class ProviderCatalogOptions
{
    public const string SectionPath = "Providers:Catalog";

    public List<ProviderCatalogEntry> Catalog { get; init; } = new();
}

/// <summary>
/// A single row in the declarative provider catalog, as expressed in
/// configuration (e.g. appsettings JSON). Field names mirror
/// <c>provider_catalog</c> columns (snake_case in config).
/// </summary>
public sealed class ProviderCatalogEntry
{
    public string ProviderId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public string[] Capabilities { get; init; } = Array.Empty<string>();
    public int CacheTtlSeconds { get; init; }
    public int TimeoutSeconds { get; init; }
    /// <summary><c>real_time</c>, <c>imported</c>, or <c>local_db</c>.</summary>
    public string SourceType { get; init; } = "imported";
    public string Version { get; init; } = "1.0.0";
}
