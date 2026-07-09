using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Providers.ViaCep;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PropertyIntelligence.Tests.Contract;

/// <summary>
/// Contract tests for the ViaCEP-backed address provider.
///
/// These tests stub ViaCEP with WireMock and exercise the concrete provider,
/// not HttpClient directly. T092 must make the red locality/state validation
/// case pass without changing this contract.
/// </summary>
public sealed class ViaCepProviderTests
{
    [Fact]
    public async Task FetchAsync_WithValidCep_ReturnsAddressInfoRawPayloadAndCallsExpectedViaCepPath()
    {
        using var server = WireMockServer.Start(port: 0);
        var fixture = await File.ReadAllTextAsync("Fixtures/ViaCep/01001000_happy.json");

        server.Given(Request.Create().WithPath("/ws/01001000/json/").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(fixture)
                .WithStatusCode(200));

        using var http = new HttpClient { BaseAddress = new Uri(server.Urls[0]) };
        var provider = new ViaCepAddressProvider(http, TimeSpan.FromDays(30));
        var address = RequestedSaoPauloAddress();

        var result = await provider.FetchAsync(address);

        provider.ProviderName.Should().Be("viacep");
        provider.CacheTtl.Should().Be(TimeSpan.FromDays(30));

        result.RawPayload.Should().Be(fixture, "raw provider payload must be preserved exactly for auditability");
        result.Data.City.Should().Be("São Paulo");
        result.Data.State.Should().Be("SP");
        result.Data.PostalCode.Should().Be("01001-000");
        result.Data.StreetName.Should().Be("Praça da Sé");
        result.Data.Neighborhood.Should().Be("Sé");
        result.Data.NormalizedAddress.Should().Contain("Praça da Sé");
        result.Data.NormalizedAddress.Should().Contain("São Paulo - SP");

        server.LogEntries.Should().ContainSingle(entry =>
            entry.RequestMessage != null &&
            entry.RequestMessage.Method == "GET" &&
            entry.RequestMessage.Path == "/ws/01001000/json/");
    }

    [Fact]
    public async Task FetchAsync_WhenViaCepLocalidadeOrUfDoesNotMatchRequestedAddress_ThrowsValidationError()
    {
        using var server = WireMockServer.Start(port: 0);
        var fixture = await File.ReadAllTextAsync("Fixtures/ViaCep/01001000_mismatch.json");

        server.Given(Request.Create().WithPath("/ws/01001000/json/").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(fixture)
                .WithStatusCode(200));

        using var http = new HttpClient { BaseAddress = new Uri(server.Urls[0]) };
        var provider = new ViaCepAddressProvider(http, TimeSpan.FromDays(30));
        var address = RequestedSaoPauloAddress();

        var act = () => provider.FetchAsync(address);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ViaCEP*localidade*uf*");

        server.LogEntries.Should().ContainSingle(entry =>
            entry.RequestMessage != null &&
            entry.RequestMessage.Method == "GET" &&
            entry.RequestMessage.Path == "/ws/01001000/json/");
    }

    [Fact]
    public async Task FetchAsync_WhenViaCepReturnsErroTrue_ThrowsNotFoundError()
    {
        using var server = WireMockServer.Start(port: 0);
        var fixture = await File.ReadAllTextAsync("Fixtures/ViaCep/99999999_not_found.json");

        server.Given(Request.Create().WithPath("/ws/99999999/json/").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(fixture)
                .WithStatusCode(200));

        using var http = new HttpClient { BaseAddress = new Uri(server.Urls[0]) };
        var provider = new ViaCepAddressProvider(http, TimeSpan.FromDays(30));
        var address = RequestedSaoPauloAddress() with
        {
            PostalCode = "99999-999",
            NormalizedAddress = "Rua Inexistente, 1 - Sé, São Paulo - SP, 99999-999",
        };

        var act = () => provider.FetchAsync(address);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CEP 99999999 not found in ViaCEP*");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abcdefgh")]
    public async Task FetchAsync_WithInvalidCepFormat_ThrowsBeforeCallingViaCep(string invalidCep)
    {
        using var server = WireMockServer.Start(port: 0);
        using var http = new HttpClient { BaseAddress = new Uri(server.Urls[0]) };
        var provider = new ViaCepAddressProvider(http, TimeSpan.FromDays(30));
        var address = RequestedSaoPauloAddress() with { PostalCode = invalidCep };

        var act = () => provider.FetchAsync(address);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*Invalid CEP format: '{invalidCep}'*");

        server.LogEntries.Should().BeEmpty("invalid CEPs must be rejected before any external HTTP call");
    }

    [Fact]
    public async Task FetchAsync_WithLocalResolver_UsesLocalResolverAndDoesNotCallNominatim()
    {
        using var viaServer = WireMockServer.Start(port: 0);
        using var nomServer = WireMockServer.Start(port: 0);
        var fixture = await File.ReadAllTextAsync("Fixtures/ViaCep/01001000_happy.json");

        viaServer.Given(Request.Create().WithPath("/ws/01001000/json/").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(fixture)
                .WithStatusCode(200));

        // Nominatim configured but should NOT be called when local resolver has data
        nomServer.Given(Request.Create().WithPath("/search").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody("[]")
                .WithStatusCode(200));

        using var http = new HttpClient { BaseAddress = new Uri(viaServer.Urls[0]) };
        using var nomHttp = new HttpClient { BaseAddress = new Uri(nomServer.Urls[0]) };

        var localMap = new System.Collections.Generic.Dictionary<string, (double Lat, double Lng)>
        {
            { "01001000", (-23.55052, -46.633308) }
        };

        var localResolver = new PropertyIntelligence.Providers.ViaCep.LocalCepCoordinateResolver(localMap);
        var provider = new ViaCepAddressProvider(http, TimeSpan.FromDays(30), localResolver, nomHttp);
        var address = RequestedSaoPauloAddress();

        var result = await provider.FetchAsync(address);

        result.Data.Lat.Should().BeApproximately(-23.55052, 0.00001);
        result.Data.Lng.Should().BeApproximately(-46.633308, 0.00001);

        nomServer.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_WhenLocalResolverMisses_UsesNominatimFallback()
    {
        using var viaServer = WireMockServer.Start(port: 0);
        using var nomServer = WireMockServer.Start(port: 0);
        var fixture = await File.ReadAllTextAsync("Fixtures/ViaCep/01001000_happy.json");
        var nomFixture = await File.ReadAllTextAsync("Fixtures/ViaCep/nominatim_praca_da_se.json");

        viaServer.Given(Request.Create().WithPath("/ws/01001000/json/").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(fixture)
                .WithStatusCode(200));

        nomServer.Given(Request.Create().WithPath("/search").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(nomFixture)
                .WithStatusCode(200));

        using var http = new HttpClient { BaseAddress = new Uri(viaServer.Urls[0]) };
        using var nomHttp = new HttpClient { BaseAddress = new Uri(nomServer.Urls[0]) };

        var provider = new ViaCepAddressProvider(http, TimeSpan.FromDays(30), null, nomHttp);
        var address = RequestedSaoPauloAddress();

        var result = await provider.FetchAsync(address);

        // Assert lat/lng extracted from nominatim fixture
        result.Data.Lat.Should().BeApproximately(-23.55052, 0.00001);
        result.Data.Lng.Should().BeApproximately(-46.633308, 0.00001);

        // Verify nominatim was called with expected query params
        nomServer.LogEntries.Should().ContainSingle();
        var entry = nomServer.LogEntries[0];
        entry.RequestMessage.Url.Should().Contain("format=jsonv2");
        entry.RequestMessage.Url.Should().Contain("limit=1");
        entry.RequestMessage.Url.Should().Contain("countrycodes=br");
        entry.RequestMessage.Url.Should().Contain("Brasil");
    }

    [Fact]
    public async Task FetchAsync_WhenGeocodingFails_ReturnsAddressInfoWithNullCoordinates()
    {
        using var viaServer = WireMockServer.Start(port: 0);
        using var nomServer = WireMockServer.Start(port: 0);
        var fixture = await File.ReadAllTextAsync("Fixtures/ViaCep/01001000_happy.json");

        viaServer.Given(Request.Create().WithPath("/ws/01001000/json/").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody(fixture)
                .WithStatusCode(200));

        // Nominatim returns empty array -> geocoding failure
        nomServer.Given(Request.Create().WithPath("/search").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody("[]")
                .WithStatusCode(200));

        using var http = new HttpClient { BaseAddress = new Uri(viaServer.Urls[0]) };
        using var nomHttp = new HttpClient { BaseAddress = new Uri(nomServer.Urls[0]) };

        var provider = new ViaCepAddressProvider(http, TimeSpan.FromDays(30), null, nomHttp);
        var address = RequestedSaoPauloAddress();

        var result = await provider.FetchAsync(address);

        result.Data.Lat.Should().BeNull();
        result.Data.Lng.Should().BeNull();
    }

    private static PropertyAddress RequestedSaoPauloAddress() => new()
    {
        PostalCode = "01001-000",
        NormalizedAddress = "Praça da Sé, 1 - Sé, São Paulo - SP, 01001-000",
        StreetName = "Praça da Sé",
        StreetNumber = "1",
        Neighborhood = "Sé",
        City = "São Paulo",
        State = "SP",
    };
}
