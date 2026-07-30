using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Mock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PropertyIntelligence.Tests.Contract;

/// <summary>
/// Contract tests for the real mobility providers.
///
/// T093 must satisfy the official São Paulo transport contract first, using
/// SPTrans + GeoSampa/Metrô/CPTM data without falling back to Overpass when
/// official sources already answer the request.
///
/// T094 must satisfy the Overpass fallback contract and stay supplementary to
/// the official provider.
/// </summary>
public sealed class TransitProvidersTests
{
    [Fact]
    public async Task OfficialTransitProvider_WithOfficialTransportFixtures_ReturnsTransitCountsAndDoesNotCallOverpass()
    {
        using var server = WireMockServer.Start(port: 0);
        var spTransFixture = await File.ReadAllTextAsync("Fixtures/Transit/sptrans_stops_happy.json");
        var geoSampaFixture = await File.ReadAllTextAsync("Fixtures/Transit/geosampa_stations_happy.json");

        server.Given(Request.Create().WithPath("/sptrans/stops").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(spTransFixture)
                .WithStatusCode(200));

        server.Given(Request.Create().WithPath("/geosampa/stations").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(geoSampaFixture)
                .WithStatusCode(200));

        server.Given(Request.Create().WithPath("/api/interpreter").UsingPost())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody("{\"elements\":[]}")
                .WithStatusCode(200));

        using var http = new HttpClient { BaseAddress = new Uri(server.Urls[0]) };
        var provider = CreateProvider("PropertyIntelligence.Providers.Transit.SpTransGeoSampaTransitProvider", http);

        var result = await provider.FetchAsync(RequestedSaoPauloAddress());

        provider.CacheTtl.Should().Be(TimeSpan.FromDays(7));
        result.Data.TransitStops500m.Should().Be(4);
        result.Data.TransitStops1km.Should().Be(8);
        result.Data.Pois2km.Should().Be(0);
        result.Data.Supermarkets1km.Should().Be(0);
        result.Data.Pharmacies1km.Should().Be(0);
        result.Data.Parks1km.Should().Be(0);
        result.Data.MobilityTrend.Should().BeNull();

        using var rawPayload = JsonDocument.Parse(result.RawPayload);
        rawPayload.RootElement.TryGetProperty("sptrans", out var sptrans).Should().BeTrue();
        rawPayload.RootElement.TryGetProperty("geosampa", out var geosampa).Should().BeTrue();
        sptrans.GetProperty("stops").GetArrayLength().Should().Be(6);
        geosampa.GetProperty("stations").GetArrayLength().Should().Be(2);

        var requests = server.LogEntries
            .Select(entry => new
            {
                Method = entry.RequestMessage?.Method,
                Path = entry.RequestMessage?.Path,
                Body = entry.RequestMessage?.Body,
            })
            .ToArray();

        requests.Should().Contain(request => request.Method == "GET" && request.Path == "/sptrans/stops");
        requests.Should().Contain(request => request.Method == "GET" && request.Path == "/geosampa/stations");
        requests.Should().NotContain(request => request.Path == "/api/interpreter");
    }

    [Fact]
    public async Task OverpassFallbackProvider_WithFallbackFixture_ReturnsPoiCountsAndCallsInterpreterWithTransitRadii()
    {
        using var server = WireMockServer.Start(port: 0);
        var fixture = await File.ReadAllTextAsync("Fixtures/Transit/overpass_transit_happy.json");

        server.Given(Request.Create().WithPath("/api/interpreter").UsingPost())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(fixture)
                .WithStatusCode(200));

        using var http = new HttpClient { BaseAddress = new Uri(server.Urls[0]) };
        var provider = CreateProvider("PropertyIntelligence.Providers.Overpass.OverpassPoiFallbackProvider", http);

        var result = await provider.FetchAsync(RequestedSaoPauloAddress());

        provider.ProviderName.Should().Be("overpass");
        provider.CacheTtl.Should().Be(TimeSpan.FromDays(7));
        result.RawPayload.Should().Be(fixture);
        result.Data.TransitStops500m.Should().Be(2);
        result.Data.TransitStops1km.Should().Be(2);
        result.Data.Pois2km.Should().Be(5);
        result.Data.Supermarkets1km.Should().Be(1);
        result.Data.Pharmacies1km.Should().Be(1);
        result.Data.Parks1km.Should().Be(1);
        result.Data.MobilityTrend.Should().BeNull();

        var requests = server.LogEntries
            .Select(entry => new
            {
                Method = entry.RequestMessage?.Method,
                Path = entry.RequestMessage?.Path,
                Body = entry.RequestMessage?.Body ?? string.Empty,
            })
            .ToArray();

        requests.Should().ContainSingle(request =>
            request.Method == "POST" &&
            request.Path == "/api/interpreter" &&
            request.Body.Contains("around:500", StringComparison.Ordinal) &&
            request.Body.Contains("around:1000", StringComparison.Ordinal) &&
            request.Body.Contains("around:2000", StringComparison.Ordinal) &&
            request.Body.Contains("\"highway\"=\"bus_stop\"", StringComparison.Ordinal));
    }

    private static IDataProvider<PoiData> CreateProvider(string fullTypeName, HttpClient httpClient)
    {
        var providerType = typeof(MockMobilityProvider).Assembly.GetType(fullTypeName);
        providerType.Should().NotBeNull($"expected mobility provider type '{fullTypeName}' to exist");
        typeof(IDataProvider<PoiData>).IsAssignableFrom(providerType).Should().BeTrue(
            $"{fullTypeName} must implement IDataProvider<PoiData>");

        return Activator.CreateInstance(providerType!, httpClient, TimeSpan.FromDays(7))
            .Should().BeAssignableTo<IDataProvider<PoiData>>()
            .Subject;
    }

    private static PropertyAddress RequestedSaoPauloAddress() => new()
    {
        NormalizedAddress = "Praça da Sé, 1 - Sé, São Paulo - SP, 01001-000",
        StreetName = "Praça da Sé",
        StreetNumber = "1",
        Neighborhood = "Sé",
        City = "São Paulo",
        State = "SP",
        PostalCode = "01001-000",
        Lat = -23.55052,
        Lng = -46.633308,
    };
}
