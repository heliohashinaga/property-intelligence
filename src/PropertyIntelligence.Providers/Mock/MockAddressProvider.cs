using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// Mock address provider — returns deterministic <see cref="AddressInfo"/> from
/// embedded fixtures. Registered as <c>mock_address</c> in the provider registry.
/// </summary>
public sealed class MockAddressProvider : IDataProvider<AddressInfo>
{
    private readonly MockProviderDataLoader _loader;
    private readonly TimeSpan _cacheTtl;

    /// <inheritdoc />
    public string ProviderName => "mock_address";

    /// <inheritdoc />
    /// <remarks>Resolved from <c>IProviderRegistry</c> at DI registration time.</remarks>
    public TimeSpan CacheTtl => _cacheTtl;

    public MockAddressProvider(MockProviderDataLoader loader, TimeSpan cacheTtl)
    {
        _loader   = loader;
        _cacheTtl = cacheTtl;
    }

    /// <inheritdoc />
    public Task<AddressInfo> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var data = _loader.Load<AddressInfo>("mock_address", address.NormalizedAddress);
        return Task.FromResult(data ?? new AddressInfo
        {
            NormalizedAddress = address.NormalizedAddress,
            City              = address.City,
            State             = address.State,
        });
    }
}