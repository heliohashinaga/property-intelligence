using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// Mock health provider — returns deterministic <see cref="HealthData"/> from
/// embedded fixtures. Registered as <c>mock_health</c> in the provider registry.
/// </summary>
public sealed class MockHealthProvider : IDataProvider<HealthData>
{
    private readonly MockProviderDataLoader _loader;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "mock_health";

    /// <remarks>Resolved from <c>IProviderRegistry</c> at DI registration time.</remarks>
    public TimeSpan CacheTtl => _cacheTtl;

    public MockHealthProvider(MockProviderDataLoader loader, TimeSpan cacheTtl)
    {
        _loader = loader;
        _cacheTtl = cacheTtl;
    }

    public Task<ProviderFetchResult<HealthData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        => Task.FromResult(_loader.LoadResult("mock_health", address.NormalizedAddress, () => new HealthData
        {
            HospitalsWithin2km = 2,
            ClinicsWith2km = 4,
            EmergencyUnits2km = 1,
            InfrastructureTrend = TrendDirection.Stable,
        }));
}
