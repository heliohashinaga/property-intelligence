using Microsoft.Extensions.DependencyInjection;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Mock;

/// <summary>
/// DI registration helpers for the mock provider layer (T018–T025).
/// </summary>
public static class MockProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers all 8 mock providers as <see cref="IDataProvider{TResult}"/> services.
    ///
    /// <para><b>CacheTtl resolution:</b> Each adapter's TTL is resolved from the
    /// <see cref="IProviderRegistry"/> at DI factory resolution time — the factory
    /// lambda calls <c>sp.GetRequiredService&lt;IProviderRegistry&gt;().Get(id)?.CacheTtl</c>
    /// and falls back to a 30-day default if the registry entry is missing.
    /// This means TTL is configuration-driven (registry metadata), not hardcoded
    /// in the adapter. The <c>IProviderRegistry</c> singleton MUST be registered
    /// before this method is called (it already is in Program.cs — T086).</para>
    ///
    /// <para>Only call when <c>Providers:Profile == "mock"</c> (or for tests).</para>
    /// </summary>
    public static IServiceCollection AddMockProviders(this IServiceCollection services)
    {
        services.AddSingleton<MockProviderDataLoader>();

        services.AddSingleton<IDataProvider<AddressInfo>, MockAddressProvider>(sp =>
            new MockAddressProvider(
                sp.GetRequiredService<MockProviderDataLoader>(),
                ResolveTtl(sp, "mock_address", 2592000)));

        services.AddSingleton<IDataProvider<PoiData>, MockMobilityProvider>(sp =>
            new MockMobilityProvider(
                sp.GetRequiredService<MockProviderDataLoader>(),
                ResolveTtl(sp, "mock_mobility", 604800)));

        services.AddSingleton<IDataProvider<FloodRiskData>, MockEnvironmentProvider>(sp =>
            new MockEnvironmentProvider(
                sp.GetRequiredService<MockProviderDataLoader>(),
                ResolveTtl(sp, "mock_environment", 2592000)));

        services.AddSingleton<IDataProvider<CrimeData>, MockSecurityProvider>(sp =>
            new MockSecurityProvider(
                sp.GetRequiredService<MockProviderDataLoader>(),
                ResolveTtl(sp, "mock_security", 86400)));

        services.AddSingleton<IDataProvider<HealthData>, MockHealthProvider>(sp =>
            new MockHealthProvider(
                sp.GetRequiredService<MockProviderDataLoader>(),
                ResolveTtl(sp, "mock_health", 604800)));

        services.AddSingleton<IDataProvider<SchoolData>, MockSchoolProvider>(sp =>
            new MockSchoolProvider(
                sp.GetRequiredService<MockProviderDataLoader>(),
                ResolveTtl(sp, "mock_school", 2592000)));

        services.AddSingleton<IDataProvider<IptuData>, MockAppreciationProvider>(sp =>
            new MockAppreciationProvider(
                sp.GetRequiredService<MockProviderDataLoader>(),
                ResolveTtl(sp, "mock_appreciation", 2592000)));

        services.AddSingleton<IDataProvider<CensusData>, MockUrbanContextProvider>(sp =>
            new MockUrbanContextProvider(
                sp.GetRequiredService<MockProviderDataLoader>(),
                ResolveTtl(sp, "mock_urban_context", 2592000)));

        return services;
    }

    /// <summary>
    /// Resolves a provider's cache TTL from the <see cref="IProviderRegistry"/>.
    /// Falls back to <paramref name="defaultSeconds"/> if the registry or
    /// the specific entry is unavailable.
    /// </summary>
    private static TimeSpan ResolveTtl(IServiceProvider sp, string providerId, int defaultSeconds)
    {
        var registry = sp.GetService<IProviderRegistry>();
        var ttl = registry?.Get(providerId)?.CacheTtl;
        return ttl ?? TimeSpan.FromSeconds(defaultSeconds);
    }
}
