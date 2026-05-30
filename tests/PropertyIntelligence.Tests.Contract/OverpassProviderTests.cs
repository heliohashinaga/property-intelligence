using PropertyIntelligence.Core.Domain;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace PropertyIntelligence.Tests.Contract;

/// <summary>
/// Contract test: OverpassPoiProvider parses Overpass API responses and
/// correctly counts POIs per radius bucket (500m / 1km / 2km).
/// RED PHASE — OverpassPoiProvider not implemented yet (T019).
/// </summary>
[Trait("Category", "Contract")]
public sealed class OverpassProviderTests : IDisposable
{
    private readonly WireMockServer _stub;

    // Fixture: 1 metro station + 2 bus stops + 1 hospital in response
    private static readonly object OverpassFixture = new
    {
        version = 0.6,
        generator = "Overpass API",
        elements = new object[]
        {
            new { type = "node", id = 1, lat = -23.5505, lon = -46.6540,
                  tags = new { railway = "station", station = "subway" } },
            new { type = "node", id = 2, lat = -23.5510, lon = -46.6545,
                  tags = new { highway = "bus_stop" } },
            new { type = "node", id = 3, lat = -23.5512, lon = -46.6548,
                  tags = new { highway = "bus_stop" } },
            new { type = "node", id = 4, lat = -23.5600, lon = -46.6600,
                  tags = new { amenity = "hospital" } }
        }
    };

    public OverpassProviderTests()
    {
        _stub = WireMockServer.Start();

        // Overpass interpreter accepts POST with QL query body
        _stub.Given(Request.Create().WithPath("/api/interpreter").UsingPost())
             .RespondWith(Response.Create()
                 .WithStatusCode(200)
                 .WithBodyAsJson(OverpassFixture));

        // Empty response for edge case tests
        _stub.Given(Request.Create().WithPath("/api/interpreter/empty").UsingPost())
             .RespondWith(Response.Create()
                 .WithStatusCode(200)
                 .WithBodyAsJson(new { version = 0.6, elements = Array.Empty<object>() }));
    }

    public void Dispose() => _stub.Stop();

    [Fact(Skip = "Red phase — OverpassPoiProvider not implemented yet (T019)")]
    public async Task FetchAsync_WithNearbyTransit_CountsCorrectly()
    {
        // Arrange
        // var provider = new OverpassPoiProvider(
        //     httpClientFactory: ...,   // base URL = _stub.Urls[0]
        //     cacheService: ...,
        //     logger: ...);
        var address = new PropertyAddress
        {
            NormalizedAddress = "Rua Augusta, 1500 - Consolação, São Paulo - SP",
            City = "São Paulo",
            State = "SP",
            Lat = -23.5563,
            Lng = -46.6543
        };

        // Act
        // var result = await provider.FetchAsync(address);

        // Assert — transit stops within 500m should include metro + bus stops
        // result.ShouldNotBeNull();
        // result.TransitStops500m.ShouldBeGreaterThanOrEqualTo(1);  // metro station
        // result.TransitStops1km.ShouldBeGreaterThanOrEqualTo(3);   // metro + 2 bus stops
        // result.Pois2km.ShouldBeGreaterThanOrEqualTo(4);           // all 4 elements

        await Task.CompletedTask;
    }

    [Fact(Skip = "Red phase — OverpassPoiProvider not implemented yet (T019)")]
    public async Task FetchAsync_EmptyArea_ReturnsZeroCounts()
    {
        // When Overpass returns no elements, all counts should be 0 (not null/exception)
        // var result = await provider.FetchAsync(address);
        // result.TransitStops500m.ShouldBe(0);
        // result.TransitStops1km.ShouldBe(0);
        // result.Pois2km.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Fact(Skip = "Red phase — OverpassPoiProvider not implemented yet (T019)")]
    public async Task FetchAsync_ProviderMetadata_IsCorrect()
    {
        // provider.ProviderName.ShouldBe("overpass");
        // provider.CacheTtl.ShouldBe(TimeSpan.FromDays(7));
        await Task.CompletedTask;
    }

    [Fact(Skip = "Red phase — OverpassPoiProvider not implemented yet (T019)")]
    public async Task FetchAsync_OverpassTimeout_DoesNotThrow()
    {
        // When Overpass returns 503, provider should return null (graceful degradation)
        // The enrichment module catches exceptions and records ProvidersUnavailable
        // var result = await provider.FetchAsync(address, ct: CancellationToken.None);
        // result.ShouldBeNull() OR exception caught by EnrichmentModule
        await Task.CompletedTask;
    }
}
