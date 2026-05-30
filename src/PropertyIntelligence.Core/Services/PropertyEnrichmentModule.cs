using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
namespace PropertyIntelligence.Core.Services;

/// <summary>
/// Runs all 7 enrichment providers in parallel (Overpass, ANA, IBGE, Crime, CNES, INEP, IPTU).
/// Address normalization (ViaCEP) happens before enrichment in the endpoint orchestration.
/// Each provider has a 5-second individual timeout; failures are recorded in
/// <see cref="PropertyProfile.ProvidersUnavailable"/> and never propagate (Constitution §V).
/// </summary>
public sealed class PropertyEnrichmentModule
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(5);

    private readonly IDataProvider<PoiData>         _overpass;
    private readonly IDataProvider<FloodRiskData>   _ana;
    private readonly IDataProvider<CensusData>      _ibge;
    private readonly IDataProvider<CrimeData>       _crime;
    private readonly IDataProvider<HealthData>      _cnes;
    private readonly IDataProvider<SchoolData>      _inep;
    private readonly IDataProvider<IptuData> _iptu;
    private readonly ILogger<PropertyEnrichmentModule> _logger;

    public PropertyEnrichmentModule(
        IDataProvider<PoiData>         overpass,
        IDataProvider<FloodRiskData>   ana,
        IDataProvider<CensusData>      ibge,
        IDataProvider<CrimeData>       crime,
        IDataProvider<HealthData>      cnes,
        IDataProvider<SchoolData>      inep,
        IDataProvider<IptuData> iptu,
        ILogger<PropertyEnrichmentModule> logger)
    {
        _overpass = overpass;
        _ana      = ana;
        _ibge     = ibge;
        _crime    = crime;
        _cnes     = cnes;
        _inep     = inep;
        _iptu     = iptu;
        _logger   = logger;
    }

    /// <summary>
    /// Enriches <paramref name="address"/> by calling all 7 providers in parallel.
    /// Each provider has a 5-second individual timeout; failures are recorded in
    /// <see cref="PropertyProfile.ProvidersUnavailable"/> and never propagate.
    /// </summary>
    public async Task<PropertyProfile> EnrichAsync(
        PropertyAddress address,
        string correlationId,
        CancellationToken ct = default)
    {
        var unavailable = new List<string>();

        var poiTask    = SafeFetchAsync(_overpass, address, ct, unavailable, correlationId);
        var floodTask  = SafeFetchAsync(_ana,      address, ct, unavailable, correlationId);
        var censusTask = SafeFetchAsync(_ibge,     address, ct, unavailable, correlationId);
        var crimeTask  = SafeFetchAsync(_crime,    address, ct, unavailable, correlationId);
        var healthTask = SafeFetchAsync(_cnes,     address, ct, unavailable, correlationId);
        var schoolTask = SafeFetchAsync(_inep,     address, ct, unavailable, correlationId);
        var iptuTask   = SafeFetchAsync(_iptu,     address, ct, unavailable, correlationId);

        await Task.WhenAll(poiTask, floodTask, censusTask, crimeTask, healthTask, schoolTask, iptuTask);

        return new PropertyProfile
        {
            Address              = address,
            PoiData              = await poiTask,
            FloodRisk            = await floodTask,
            CensusData           = await censusTask,
            CrimeData            = await crimeTask,
            HealthData           = await healthTask,
            SchoolData           = await schoolTask,
            IptuData             = await iptuTask,
            ProvidersUnavailable = unavailable,
            AllFromCache         = false,
        };
    }

    private async Task<TResult?> SafeFetchAsync<TResult>(
        IDataProvider<TResult> provider,
        PropertyAddress address,
        CancellationToken ct,
        List<string> unavailable,
        string correlationId)
        where TResult : class
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProviderTimeout);

            var result = await provider.FetchAsync(address, cts.Token);
            _logger.LogDebug(
                "Provider {Provider} completed in {Ms}ms. CorrelationId={CorrelationId}",
                provider.ProviderName, sw.ElapsedMilliseconds, correlationId);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Provider {Provider} timed out after {Ms}ms. CorrelationId={CorrelationId}",
                provider.ProviderName, sw.ElapsedMilliseconds, correlationId);
            lock (unavailable) unavailable.Add(provider.ProviderName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Provider {Provider} failed after {Ms}ms. CorrelationId={CorrelationId}",
                provider.ProviderName, sw.ElapsedMilliseconds, correlationId);
            lock (unavailable) unavailable.Add(provider.ProviderName);
            return null;
        }
    }
}
