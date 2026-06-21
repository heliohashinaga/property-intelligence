using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// Mock appreciation provider — returns deterministic <see cref="IptuData"/> from
/// embedded fixtures. Registered as <c>mock_appreciation</c> in the provider registry.
/// </summary>
public sealed class MockAppreciationProvider : IDataProvider<IptuData>
{
    private readonly MockProviderDataLoader _loader;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "mock_appreciation";

    /// <remarks>Resolved from <c>IProviderRegistry</c> at DI registration time.</remarks>
    public TimeSpan CacheTtl => _cacheTtl;

    public MockAppreciationProvider(MockProviderDataLoader loader, TimeSpan cacheTtl)
    {
        _loader   = loader;
        _cacheTtl = cacheTtl;
    }

    public Task<IptuData> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var data = _loader.Load<IptuData>("mock_appreciation", address.NormalizedAddress);
        return Task.FromResult(data ?? new IptuData
        {
            ValorVenal          = 750000,
            ZoningClass         = "ZM-1",
            AppreciationTrend   = TrendDirection.Stable,
        });
    }
}