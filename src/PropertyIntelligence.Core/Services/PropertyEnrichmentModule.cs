using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Registry;

namespace PropertyIntelligence.Core.Services;

/// <summary>
/// Orchestrates data-source enrichment for a property address.
///
/// <para><b>Registry-driven:</b> iterates ONLY the providers returned by
/// <see cref="IProviderRegistry.GetEnabled()"/> — disabled registry entries are
/// skipped BEFORE any fan-out. Per-provider timeout is resolved from the
/// registry descriptor (<see cref="ProviderDescriptor.Timeout"/>), not from a
/// hardcoded provider switch. Cache TTL is likewise registry-derived.</para>
/// </summary>
public sealed class PropertyEnrichmentModule
{
    private readonly IProviderRegistry _registry;
    private readonly IDataProviderRawLogStore _rawLogStore;
    private readonly ILogger<PropertyEnrichmentModule> _logger;
    private readonly IReadOnlyDictionary<string, Func<PropertyAddress, CancellationToken, Task<(object? Payload, string RawPayload)>>> _providerInvokers;

    public PropertyEnrichmentModule(
        IProviderRegistry registry,
        IDataProviderRawLogStore rawLogStore,
        IEnumerable<IDataProvider<AddressInfo>> addressProviders,
        IEnumerable<IDataProvider<PoiData>> poiProviders,
        IEnumerable<IDataProvider<FloodRiskData>> floodRiskProviders,
        IEnumerable<IDataProvider<CensusData>> censusProviders,
        IEnumerable<IDataProvider<CrimeData>> crimeProviders,
        IEnumerable<IDataProvider<HealthData>> healthProviders,
        IEnumerable<IDataProvider<SchoolData>> schoolProviders,
        IEnumerable<IDataProvider<IptuData>> iptuProviders,
        ILogger<PropertyEnrichmentModule> logger)
    {
        _registry = registry;
        _rawLogStore = rawLogStore;
        _logger = logger;
        _providerInvokers = BuildProviderInvokers(
            addressProviders,
            poiProviders,
            floodRiskProviders,
            censusProviders,
            crimeProviders,
            healthProviders,
            schoolProviders,
            iptuProviders);
    }

    /// <summary>
    /// Resolves enabled providers from the registry, executes them in parallel
    /// with per-provider timeouts, aggregates successful payloads into a
    /// <see cref="PropertyProfile"/>, and records raw payload logs for audit.
    /// </summary>
    public async Task<PropertyProfile> EnrichAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var enabled = _registry.GetEnabled();
        var executions = enabled
            .Select(descriptor => ExecuteProviderAsync(descriptor, address, ct))
            .ToArray();

        var results = await Task.WhenAll(executions);

        AddressInfo? addressInfo = null;
        PoiData? poiData = null;
        FloodRiskData? floodRisk = null;
        CensusData? censusData = null;
        CrimeData? crimeData = null;
        HealthData? healthData = null;
        SchoolData? schoolData = null;
        IptuData? iptuData = null;

        var unavailableProviders = new List<string>();
        var rawLogs = new List<DataProviderRawLog>(results.Length);

        foreach (var result in results)
        {
            rawLogs.Add(new DataProviderRawLog
            {
                AddressId = address.Id,
                ProviderName = result.ProviderId,
                RawPayload = result.RawPayload,
            });

            if (!result.Success)
            {
                unavailableProviders.Add(result.ProviderId);
                continue;
            }

            switch (result.Payload)
            {
                case AddressInfo value:
                    addressInfo = value;
                    break;
                case PoiData value:
                    poiData = value;
                    break;
                case FloodRiskData value:
                    floodRisk = value;
                    break;
                case CensusData value:
                    censusData = value;
                    break;
                case CrimeData value:
                    crimeData = value;
                    break;
                case HealthData value:
                    healthData = value;
                    break;
                case SchoolData value:
                    schoolData = value;
                    break;
                case IptuData value:
                    iptuData = value;
                    break;
            }
        }

        await _rawLogStore.SaveAsync(rawLogs, ct);

        return new PropertyProfile
        {
            Address = address,
            AddressInfo = addressInfo,
            PoiData = poiData,
            FloodRisk = floodRisk,
            CensusData = censusData,
            CrimeData = crimeData,
            HealthData = healthData,
            SchoolData = schoolData,
            IptuData = iptuData,
            ProvidersUnavailable = unavailableProviders,
            AllFromCache = false,
        };
    }

    private async Task<ProviderExecutionResult> ExecuteProviderAsync(
        ProviderDescriptor descriptor,
        PropertyAddress address,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Registry selected provider {ProviderId} (timeout={Timeout}s, ttl={Ttl}s).",
            descriptor.ProviderId,
            descriptor.Timeout.TotalSeconds,
            descriptor.CacheTtl.TotalSeconds);

        if (!_providerInvokers.TryGetValue(descriptor.ProviderId, out var invoker))
        {
            _logger.LogWarning("No IDataProvider registration found for enabled provider {ProviderId}.", descriptor.ProviderId);
            return ProviderExecutionResult.Failure(
                descriptor.ProviderId,
                JsonSerializer.Serialize(new { provider = descriptor.ProviderId, error = "provider_not_registered" }));
        }

        using var perProviderCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        perProviderCts.CancelAfter(descriptor.Timeout);

        try
        {
            var fetchResult = await invoker(address, perProviderCts.Token);
            return ProviderExecutionResult.SuccessResult(descriptor.ProviderId, fetchResult.Payload, fetchResult.RawPayload);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Provider {ProviderId} timed out after {TimeoutSeconds}s.",
                descriptor.ProviderId,
                descriptor.Timeout.TotalSeconds);

            return ProviderExecutionResult.Failure(
                descriptor.ProviderId,
                JsonSerializer.Serialize(new { provider = descriptor.ProviderId, error = "timeout" }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {ProviderId} failed during enrichment.", descriptor.ProviderId);
            return ProviderExecutionResult.Failure(
                descriptor.ProviderId,
                JsonSerializer.Serialize(new { provider = descriptor.ProviderId, error = ex.Message }));
        }
    }

    private static IReadOnlyDictionary<string, Func<PropertyAddress, CancellationToken, Task<(object? Payload, string RawPayload)>>> BuildProviderInvokers(
        IEnumerable<IDataProvider<AddressInfo>> addressProviders,
        IEnumerable<IDataProvider<PoiData>> poiProviders,
        IEnumerable<IDataProvider<FloodRiskData>> floodRiskProviders,
        IEnumerable<IDataProvider<CensusData>> censusProviders,
        IEnumerable<IDataProvider<CrimeData>> crimeProviders,
        IEnumerable<IDataProvider<HealthData>> healthProviders,
        IEnumerable<IDataProvider<SchoolData>> schoolProviders,
        IEnumerable<IDataProvider<IptuData>> iptuProviders)
    {
        var invokers = new Dictionary<string, Func<PropertyAddress, CancellationToken, Task<(object? Payload, string RawPayload)>>>(StringComparer.Ordinal);

        RegisterProviders(invokers, addressProviders);
        RegisterProviders(invokers, poiProviders);
        RegisterProviders(invokers, floodRiskProviders);
        RegisterProviders(invokers, censusProviders);
        RegisterProviders(invokers, crimeProviders);
        RegisterProviders(invokers, healthProviders);
        RegisterProviders(invokers, schoolProviders);
        RegisterProviders(invokers, iptuProviders);

        return invokers;
    }

    private static void RegisterProviders<T>(
        IDictionary<string, Func<PropertyAddress, CancellationToken, Task<(object? Payload, string RawPayload)>>> invokers,
        IEnumerable<IDataProvider<T>> providers)
    {
        foreach (var provider in providers)
        {
            invokers[provider.ProviderName] = async (address, ct) =>
            {
                var result = await provider.FetchAsync(address, ct);
                return (result.Data, result.RawPayload);
            };
        }
    }

    private sealed record ProviderExecutionResult(string ProviderId, bool Success, object? Payload, string RawPayload)
    {
        public static ProviderExecutionResult SuccessResult(string providerId, object? payload, string rawPayload)
            => new(providerId, true, payload, rawPayload);

        public static ProviderExecutionResult Failure(string providerId, string rawPayload)
            => new(providerId, false, null, rawPayload);
    }
}
