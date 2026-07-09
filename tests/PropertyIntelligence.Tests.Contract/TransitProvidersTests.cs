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
            var csv = await File.ReadAllTextAsync("Fixtures/Transit/official_sptrans_sampacsv.csv");

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

            var csv = await File.ReadAllTextAsync("Fixtures/Transit/official_sptrans_sampacsv.csv");
            officialServer.Given(Request.Create().WithPath("/sptrans/stops.csv").UsingGet())
                .RespondWith(Response.Create().WithHeader("Content-Type", "text/csv").WithBody(csv).WithStatusCode(200));

            overpassServer.Given(Request.Create().WithPath("/api/interpreter").UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithBody(File.ReadAllText("Fixtures/Transit/overpass_pois.geojson")));

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

    }
}
