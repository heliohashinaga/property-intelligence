using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// Mock environment provider — returns deterministic <see cref="FloodRiskData"/> from
/// embedded fixtures. Registered as <c>mock_environment</c> in the provider registry.
/// </summary>
public sealed class MockEnvironmentProvider : IDataProvider<FloodRiskData>
{
    private readonly MockProviderDataLoader _loader;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "mock_environment";

    /// <remarks>Resolved from <c>IProviderRegistry</c> at DI registration time.</remarks>
    public TimeSpan CacheTtl => _cacheTtl;

    public MockEnvironmentProvider(MockProviderDataLoader loader, TimeSpan cacheTtl)
    {
        _loader = loader;
        _cacheTtl = cacheTtl;
    }

    public Task<ProviderFetchResult<FloodRiskData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        => Task.FromResult(_loader.LoadResult("mock_environment", address.NormalizedAddress, () => new FloodRiskData
        {
            RiskLevel = null,
            DistanceMetres = null,
            Trend = TrendDirection.Stable,
        }));
}
