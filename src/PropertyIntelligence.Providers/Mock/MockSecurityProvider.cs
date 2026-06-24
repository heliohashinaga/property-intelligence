using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// Mock security provider — returns deterministic <see cref="CrimeData"/> from
/// embedded fixtures. Registered as <c>mock_security</c> in the provider registry.
/// </summary>
public sealed class MockSecurityProvider : IDataProvider<CrimeData>
{
    private readonly MockProviderDataLoader _loader;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "mock_security";

    /// <remarks>Resolved from <c>IProviderRegistry</c> at DI registration time.</remarks>
    public TimeSpan CacheTtl => _cacheTtl;

    public MockSecurityProvider(MockProviderDataLoader loader, TimeSpan cacheTtl)
    {
        _loader = loader;
        _cacheTtl = cacheTtl;
    }

    public Task<ProviderFetchResult<CrimeData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        => Task.FromResult(_loader.LoadResult("mock_security", address.NormalizedAddress, () => new CrimeData
        {
            CrimeRatePer100k = 2500,
            YoyChangePct = 0,
            SecurityTrend = TrendDirection.Stable,
        }));
}
