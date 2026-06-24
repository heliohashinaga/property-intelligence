using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// Mock mobility provider — returns deterministic <see cref="PoiData"/> from
/// embedded fixtures. Registered as <c>mock_mobility</c> in the provider registry.
/// </summary>
public sealed class MockMobilityProvider : IDataProvider<PoiData>
{
    private readonly MockProviderDataLoader _loader;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "mock_mobility";

    /// <remarks>Resolved from <c>IProviderRegistry</c> at DI registration time.</remarks>
    public TimeSpan CacheTtl => _cacheTtl;

    public MockMobilityProvider(MockProviderDataLoader loader, TimeSpan cacheTtl)
    {
        _loader = loader;
        _cacheTtl = cacheTtl;
    }

    public Task<ProviderFetchResult<PoiData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        => Task.FromResult(_loader.LoadResult("mock_mobility", address.NormalizedAddress, () => new PoiData
        {
            TransitStops500m = 10,
            TransitStops1km = 20,
            Pois2km = 200,
            Supermarkets1km = 3,
            Pharmacies1km = 4,
            Parks1km = 1,
            MobilityTrend = TrendDirection.Stable,
        }));
}
