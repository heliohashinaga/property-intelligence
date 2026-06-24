using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// Mock urban context provider — returns deterministic <see cref="CensusData"/> from
/// embedded fixtures. Registered as <c>mock_urban_context</c> in the provider registry.
/// </summary>
public sealed class MockUrbanContextProvider : IDataProvider<CensusData>
{
    private readonly MockProviderDataLoader _loader;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "mock_urban_context";

    /// <remarks>Resolved from <c>IProviderRegistry</c> at DI registration time.</remarks>
    public TimeSpan CacheTtl => _cacheTtl;

    public MockUrbanContextProvider(MockProviderDataLoader loader, TimeSpan cacheTtl)
    {
        _loader = loader;
        _cacheTtl = cacheTtl;
    }

    public Task<ProviderFetchResult<CensusData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        => Task.FromResult(_loader.LoadResult("mock_urban_context", address.NormalizedAddress, () => new CensusData
        {
            MedianIncomeGroup = 3,
            PopulationDensity = 8000,
            WorkingAgePct = 70,
            UrbanContextTrend = TrendDirection.Stable,
        }));
}
