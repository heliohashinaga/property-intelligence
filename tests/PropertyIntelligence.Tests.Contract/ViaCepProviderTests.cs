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
