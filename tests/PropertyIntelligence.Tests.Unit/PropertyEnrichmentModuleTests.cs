using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Registry;
using PropertyIntelligence.Core.Services;

namespace PropertyIntelligence.Tests.Unit;

/// <summary>
/// T017 — red tests for the enrichment module's registry-driven fan-out.
/// These tests describe the intended behavior before T026 implements it.
/// </summary>
public sealed class PropertyEnrichmentModuleTests
{
    private static readonly PropertyAddress SampleAddress = new()
    {
        NormalizedAddress = "Rua Augusta, 1500 - Consolação, São Paulo - SP",
        StreetName = "Rua Augusta",
        StreetNumber = "1500",
        Neighborhood = "Consolação",
        City = "São Paulo",
        State = "SP",
        PostalCode = "01310100",
        Lat = -23.5563,
        Lng = -46.6543,
    };

    [Fact]
    public async Task EnrichAsync_skips_disabled_providers_before_fan_out()
    {
        var addressProvider = new TrackingAddressProvider();
        var disabledSecurityProvider = new TrackingCrimeProvider("mock_security");
        var rawLogStore = new TrackingRawLogStore();

        using var services = BuildServices(
            descriptors:
            [
                Descriptor("mock_address", enabled: true, capability: "address"),
                Descriptor("mock_security", enabled: false, capability: "security"),
            ],
            configure: collection =>
            {
                collection.AddSingleton<IDataProviderRawLogStore>(rawLogStore);
                collection.AddSingleton<IDataProvider<AddressInfo>>(addressProvider);
                collection.AddSingleton<IDataProvider<CrimeData>>(disabledSecurityProvider);
            });

        var module = services.GetRequiredService<PropertyEnrichmentModule>();
        var profile = await module.EnrichAsync(SampleAddress);

        addressProvider.CallCount.Should().Be(1);
        disabledSecurityProvider.CallCount.Should().Be(0);
        profile.AddressInfo.Should().NotBeNull();
        profile.AddressInfo!.NormalizedAddress.Should().Be(SampleAddress.NormalizedAddress);
        profile.ProvidersUnavailable.Should().BeEmpty();
        rawLogStore.Logs.Select(static log => log.ProviderName).Should().Equal("mock_address");
        rawLogStore.Logs.Select(static log => log.RawPayload).Should().Equal("{\"normalizedAddress\":\"Rua Augusta, 1500 - Consolação, São Paulo - SP\"}");
    }

    [Fact]
    public async Task EnrichAsync_records_failed_enabled_provider_as_unavailable()
    {
        var addressProvider = new TrackingAddressProvider();
        var failingSecurityProvider = new FailingCrimeProvider("mock_security");
        var rawLogStore = new TrackingRawLogStore();

        using var services = BuildServices(
            descriptors:
            [
                Descriptor("mock_address", enabled: true, capability: "address"),
                Descriptor("mock_security", enabled: true, capability: "security"),
            ],
            configure: collection =>
            {
                collection.AddSingleton<IDataProviderRawLogStore>(rawLogStore);
                collection.AddSingleton<IDataProvider<AddressInfo>>(addressProvider);
                collection.AddSingleton<IDataProvider<CrimeData>>(failingSecurityProvider);
            });

        var module = services.GetRequiredService<PropertyEnrichmentModule>();
        var profile = await module.EnrichAsync(SampleAddress);

        addressProvider.CallCount.Should().Be(1);
        failingSecurityProvider.CallCount.Should().Be(1);
        profile.AddressInfo.Should().NotBeNull();
        profile.CrimeData.Should().BeNull();
        profile.ProvidersUnavailable.Should().Equal("mock_security");
        rawLogStore.Logs.Select(static log => log.ProviderName)
            .Should().BeEquivalentTo(new[] { "mock_address", "mock_security" });
        rawLogStore.Logs.Should().ContainSingle(static log =>
            log.ProviderName == "mock_security"
            && log.RawPayload.Contains("Simulated provider failure", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildServices(
        IReadOnlyList<ProviderDescriptor> descriptors,
        Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProviderRegistry>(new FakeProviderRegistry(descriptors));
        services.AddSingleton<IDataProviderRawLogStore, TrackingRawLogStore>();
        services.AddSingleton(NullLogger<PropertyEnrichmentModule>.Instance);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<PropertyEnrichmentModule>>(
            NullLogger<PropertyEnrichmentModule>.Instance);
        configure(services);
        services.AddTransient<PropertyEnrichmentModule>();
        return services.BuildServiceProvider();
    }

    private static ProviderDescriptor Descriptor(string providerId, bool enabled, string capability) => new()
    {
        ProviderId = providerId,
        DisplayName = providerId,
        Enabled = enabled,
        Capabilities = [capability],
        CacheTtl = TimeSpan.FromDays(30),
        Timeout = TimeSpan.FromSeconds(5),
        SourceType = SourceType.Imported,
        Version = "1.0.0",
    };

    private sealed class FakeProviderRegistry(IReadOnlyList<ProviderDescriptor> descriptors) : IProviderRegistry
    {
        public IReadOnlyList<ProviderDescriptor> GetEnabled()
            => descriptors.Where(static descriptor => descriptor.Enabled).ToList();

        public ProviderDescriptor? Get(string providerId)
            => descriptors.FirstOrDefault(descriptor => descriptor.ProviderId == providerId);
    }

    private sealed class TrackingAddressProvider : IDataProvider<AddressInfo>
    {
        public string ProviderName => "mock_address";
        public TimeSpan CacheTtl => TimeSpan.FromDays(30);
        public int CallCount { get; private set; }

        public Task<ProviderFetchResult<AddressInfo>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new ProviderFetchResult<AddressInfo>
            {
                Data = new AddressInfo
                {
                    NormalizedAddress = address.NormalizedAddress,
                    StreetName = address.StreetName,
                    StreetNumber = address.StreetNumber,
                    Neighborhood = address.Neighborhood,
                    City = address.City,
                    State = address.State,
                    PostalCode = address.PostalCode,
                    Lat = address.Lat,
                    Lng = address.Lng,
                },
                RawPayload = "{\"normalizedAddress\":\"Rua Augusta, 1500 - Consolação, São Paulo - SP\"}",
            });
        }
    }

    private sealed class TrackingCrimeProvider(string providerName) : IDataProvider<CrimeData>
    {
        public string ProviderName => providerName;
        public TimeSpan CacheTtl => TimeSpan.FromDays(1);
        public int CallCount { get; private set; }

        public Task<ProviderFetchResult<CrimeData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new ProviderFetchResult<CrimeData>
            {
                Data = new CrimeData
                {
                    CrimeRatePer100k = 95,
                    YoyChangePct = -2,
                    SecurityTrend = TrendDirection.Improving,
                },
                RawPayload = "{\"crimeRatePer100k\":95,\"yoyChangePct\":-2,\"securityTrend\":\"Improving\"}",
            });
        }
    }

    private sealed class FailingCrimeProvider(string providerName) : IDataProvider<CrimeData>
    {
        public string ProviderName => providerName;
        public TimeSpan CacheTtl => TimeSpan.FromDays(1);
        public int CallCount { get; private set; }

        public Task<ProviderFetchResult<CrimeData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        {
            CallCount++;
            throw new InvalidOperationException("Simulated provider failure");
        }
    }

    private sealed class TrackingRawLogStore : IDataProviderRawLogStore
    {
        public List<DataProviderRawLog> Logs { get; } = [];

        public Task SaveAsync(IReadOnlyList<DataProviderRawLog> logs, CancellationToken ct = default)
        {
            Logs.AddRange(logs);
            return Task.CompletedTask;
        }
    }
}
