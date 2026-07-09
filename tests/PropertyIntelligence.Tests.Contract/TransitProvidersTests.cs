using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PropertyIntelligence.Tests.Contract
{
    // These contract tests are intentionally written before providers exist to lock the contract (RED tests).
    public class TransitProvidersTests
    {
        [Fact]
        public async Task SpTransGeoSampaTransitProvider_WithOfficialSaoPauloFixtures_ReturnsMobilityCountsAndRawPayload()
        {
            using var officialServer = WireMockServer.Start(port: 0);
            var csvPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Transit", "official_sptrans_sampacsv.csv");
            var csv = await File.ReadAllTextAsync(csvPath);

            officialServer.Given(Request.Create().WithPath("/sptrans/stops.csv").UsingGet())
                .RespondWith(Response.Create().WithHeader("Content-Type", "text/csv").WithBody(csv).WithStatusCode(200));

            using var http = new HttpClient { BaseAddress = new Uri(officialServer.Urls[0]) };

            // Expectation: provider exists and parses CSV into counts + raw payload
            var provider = new PropertyIntelligence.Providers.Transit.SpTransGeoSampaTransitProvider(http);
            var address = new Core.Domain.PropertyAddress { PostalCode = "01001-000", NormalizedAddress = "Praça da Sé, São Paulo - SP", City = "São Paulo", State = "SP", Lat = -23.55052, Lng = -46.633308 };
            var result = await provider.FetchAsync(address);

            Assert.NotNull(result.RawPayload);
            Assert.True(result.Data.TransitStops500m > 0);
        }

        [Fact]
        public async Task SpTransGeoSampaTransitProvider_UsesOfficialSourcesWithoutCallingOverpass()
        {
            using var officialServer = WireMockServer.Start(port: 0);
            using var overpassServer = WireMockServer.Start(port: 0);

            var csvPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Transit", "official_sptrans_sampacsv.csv");
            var csv = await File.ReadAllTextAsync(csvPath);
            officialServer.Given(Request.Create().WithPath("/sptrans/stops.csv").UsingGet())
                .RespondWith(Response.Create().WithHeader("Content-Type", "text/csv").WithBody(csv).WithStatusCode(200));

            overpassServer.Given(Request.Create().WithPath("/api/interpreter").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithBody(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Transit", "overpass_pois.geojson"))));

            using var http = new HttpClient { BaseAddress = new Uri(officialServer.Urls[0]) };
            using var overHttp = new HttpClient { BaseAddress = new Uri(overpassServer.Urls[0]) };

            var provider = new PropertyIntelligence.Providers.Transit.SpTransGeoSampaTransitProvider(http, overHttp);
            var address = new Core.Domain.PropertyAddress { PostalCode = "01001-000", NormalizedAddress = "Praça da Sé, São Paulo - SP", City = "São Paulo", State = "SP", Lat = -23.55052, Lng = -46.633308 };
            var result = await provider.FetchAsync(address);

            // should not call overpass when official coverage present
            Assert.True(officialServer.LogEntries.Count > 0);
            Assert.Empty(overpassServer.LogEntries);
            Assert.True(result.Data.TransitStops500m > 0);
        }

        [Fact]
        public async Task OverpassPoiFallbackProvider_WhenOfficialCoverageIsUnavailable_ReturnsSupplementalMobilityPoisFromOverpassStub()
        {
            using var overpassServer = WireMockServer.Start(port: 0);
            var jsonPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Transit", "overpass_pois_praca_da_se.json");
            var json = await File.ReadAllTextAsync(jsonPath);

            overpassServer.Given(Request.Create().WithPath("/api/interpreter").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(json));

            using var overHttp = new HttpClient { BaseAddress = new Uri(overpassServer.Urls[0]) };
            var provider = new PropertyIntelligence.Providers.Transit.OverpassPoiFallbackProvider(overHttp);

            var address = new Core.Domain.PropertyAddress { PostalCode = "01001-000", NormalizedAddress = "Praça da Sé, São Paulo - SP", City = "São Paulo", State = "SP", Lat = -23.55052, Lng = -46.633308 };
            var result = await provider.FetchAsync(address);

            Assert.NotNull(result.RawPayload);
            Assert.True(result.Data.TransitStops500m > 0);
            Assert.True(overpassServer.LogEntries.Count > 0);
        }

        [Fact]
        public async Task OverpassPoiFallbackProvider_WithoutCoordinates_ThrowsValidationErrorBeforeHttpCall()
        {
            using var overpassServer = WireMockServer.Start(port: 0);
            overpassServer.Given(Request.Create().WithPath("/api/interpreter").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

            using var overHttp = new HttpClient { BaseAddress = new Uri(overpassServer.Urls[0]) };
            var provider = new PropertyIntelligence.Providers.Transit.OverpassPoiFallbackProvider(overHttp);

            var address = new Core.Domain.PropertyAddress { PostalCode = "01001-000", NormalizedAddress = "Praça da Sé, São Paulo - SP", City = "São Paulo", State = "SP", Lat = null, Lng = null };

            await Assert.ThrowsAsync<ArgumentException>(async () => await provider.FetchAsync(address));
            // no HTTP calls should have been made
            Assert.Empty(overpassServer.LogEntries);
        }

    }
}
