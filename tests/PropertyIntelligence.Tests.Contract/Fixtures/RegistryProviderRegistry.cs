using System.Text.Json;
using PropertyIntelligence.Core.Registry;

namespace PropertyIntelligence.Tests.Contract.Fixtures;

/// <summary>
/// Loads <see cref="ProviderDescriptor"/> catalogs from registry fixture JSON
/// files (snake_case schema, mirroring <c>provider_catalog</c>). Used by
/// contract tests (T015/T016) to drive subset / graceful-degradation scenarios
/// without code changes.
/// </summary>
public static class RegistryProviderRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Loads ALL descriptors (enabled + disabled) from a fixture JSON file.
    /// </summary>
    public static IReadOnlyList<ProviderDescriptor> Load(string jsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"Registry fixture not found: {jsonPath}", jsonPath);

        var json = File.ReadAllText(jsonPath);
        var entries = JsonSerializer.Deserialize<List<FixtureEntry>>(json, JsonOptions)
                      ?? throw new InvalidDataException($"Empty/invalid registry fixture: {jsonPath}");

        return entries.Select(Map).ToList();
    }

    /// <summary>
    /// Loads only the enabled descriptors from a fixture JSON file.
    /// </summary>
    public static IReadOnlyList<ProviderDescriptor> LoadEnabled(string jsonPath)
        => Load(jsonPath).Where(d => d.Enabled).ToList();

    private static ProviderDescriptor Map(FixtureEntry e) => new()
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

    private sealed class FixtureEntry
    {
        public string ProviderId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        public string[]? Capabilities { get; init; }
        public int CacheTtlSeconds { get; init; }
        public int TimeoutSeconds { get; init; }
        public string SourceType { get; init; } = "imported";
        public string Version { get; init; } = "1.0.0";
    }
}