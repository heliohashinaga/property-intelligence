using PropertyIntelligence.Core.Registry;

namespace PropertyIntelligence.Providers.Registry;

/// <summary>
/// Maps <see cref="ProviderCatalogEntry"/> (config-bound) to the runtime
/// <see cref="ProviderDescriptor"/> used by <c>IProviderRegistry</c>.
/// </summary>
public static class ProviderCatalogExtensions
{
    /// <summary>
    /// Converts the configured catalog entries into runtime descriptors, in
    /// catalog order. Unknown <c>source_type</c> values fall back to
    /// <see cref="SourceType.Imported"/> (the safe default for locally-imported
    /// datasets, which is the common case during the mock MVP slice).
    /// </summary>
    public static IReadOnlyList<ProviderDescriptor> ToDescriptors(
        this IEnumerable<ProviderCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Select(Map).ToList();
    }

    private static ProviderDescriptor Map(ProviderCatalogEntry e) => new()
    {
        ProviderId   = e.ProviderId,
        DisplayName  = e.DisplayName,
        Enabled      = e.Enabled,
        Capabilities = (e.Capabilities ?? Array.Empty<string>()).ToList(),
        CacheTtl     = TimeSpan.FromSeconds(e.CacheTtlSeconds),
        Timeout      = TimeSpan.FromSeconds(e.TimeoutSeconds),
        SourceType   = MapSourceType(e.SourceType),
        Version      = e.Version,
    };

    private static SourceType MapSourceType(string? s) => s switch
    {
        "real_time" => SourceType.RealTime,
        "local_db"  => SourceType.LocalDb,
        _           => SourceType.Imported,
    };
}