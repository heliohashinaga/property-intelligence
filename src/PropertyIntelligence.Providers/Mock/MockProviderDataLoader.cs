using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// Loads deterministic fixture JSON for mock providers.
/// Fixtures are embedded resources in the Providers assembly:
///   <c>PropertyIntelligence.Providers.Mock.Fixtures.{provider_id}.json</c>
/// </summary>
public sealed class MockProviderDataLoader
{
    private readonly ILogger<MockProviderDataLoader> _logger;
    private readonly Assembly _assembly;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        Converters                  = { new JsonStringEnumConverter() },
    };

    /// <summary>Per-provider fixture cache (parse once on first access).</summary>
    private readonly ConcurrentDictionary<string, FixtureFile?> _cache = new();

    /// <summary>Container shape for each fixture file.</summary>
    private sealed class FixtureFile
    {
        public List<Scenario> Scenarios { get; init; } = new();
    }

    private sealed class Scenario
    {
        public string Address { get; init; } = string.Empty;
        public JsonElement Data { get; init; }
    }

    public MockProviderDataLoader(ILogger<MockProviderDataLoader> logger)
    {
        _logger   = logger;
        _assembly = typeof(MockProviderDataLoader).Assembly;
    }

    /// <summary>
    /// Loads the fixture data for a given provider_id and address.
    /// Address match is case-insensitive, trimmed. If no match found,
    /// returns the first scenario (fallback). Returns <c>null</c> if no
    /// fixture file exists for the provider_id.
    /// </summary>
    public T? Load<T>(string providerId, string address) where T : class
    {
        var raw = LoadRaw(providerId, address);
        if (raw is null)
            return null;

        return raw.Value.Deserialize<T>(JsonOptions);
    }

    /// <summary>
    /// Loads raw JSON for a provider_id and address, returning
    /// the <see cref="JsonElement"/> for the scenario's <c>data</c> field.
    /// Returns <c>null</c> if no fixture file exists or no scenario is found.
    /// </summary>
    public JsonElement? LoadRaw(string providerId, string address)
    {
        var fixture = _cache.GetOrAdd(providerId, LoadFixtureFile);
        if (fixture is null)
            return null;

        var normalizedAddr = address.Trim().ToLowerInvariant();

        var scenario = fixture.Scenarios.FirstOrDefault(s =>
            s.Address.Trim().ToLowerInvariant() == normalizedAddr);

        if (scenario is null)
        {
            // Fallback: return first scenario so the endpoint always works in mock mode
            scenario = fixture.Scenarios.FirstOrDefault();
            _logger.LogDebug(
                "No fixture match for provider {ProviderId} address '{Address}'; using first scenario fallback.",
                providerId, address);
        }

        if (scenario is null)
            return null;

        return scenario.Data;
    }

    /// <summary>
    /// Checks whether a fixture file exists for the given provider_id.
    /// </summary>
    public bool HasFixture(string providerId)
    {
        var resourceName = GetResourceName(providerId);
        return _assembly.GetManifestResourceNames().Contains(resourceName);
    }

    private FixtureFile? LoadFixtureFile(string providerId)
    {
        var resourceName = GetResourceName(providerId);

        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            _logger.LogWarning(
                "Fixture resource '{ResourceName}' not found for provider '{ProviderId}'.",
                resourceName, providerId);
            return null;
        }

        var json = new StreamReader(stream).ReadToEnd();
        return JsonSerializer.Deserialize<FixtureFile>(json, JsonOptions);
    }

    private static string GetResourceName(string providerId)
        => $"PropertyIntelligence.Providers.Mock.Fixtures.{providerId}.json";
}