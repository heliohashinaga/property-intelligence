using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// Mock school provider — returns deterministic <see cref="SchoolData"/> from
/// embedded fixtures. Registered as <c>mock_school</c> in the provider registry.
/// </summary>
public sealed class MockSchoolProvider : IDataProvider<SchoolData>
{
    private readonly MockProviderDataLoader _loader;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "mock_school";

    /// <remarks>Resolved from <c>IProviderRegistry</c> at DI registration time.</remarks>
    public TimeSpan CacheTtl => _cacheTtl;

    public MockSchoolProvider(MockProviderDataLoader loader, TimeSpan cacheTtl)
    {
        _loader = loader;
        _cacheTtl = cacheTtl;
    }

    public Task<ProviderFetchResult<SchoolData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        => Task.FromResult(_loader.LoadResult("mock_school", address.NormalizedAddress, () => new SchoolData
        {
            SchoolsWithin2km = 4,
            NearestSchoolIdeb = 6.0,
            InfrastructureTrend = TrendDirection.Stable,
        }));
}
