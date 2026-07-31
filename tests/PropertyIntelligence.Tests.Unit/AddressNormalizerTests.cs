using System.Net;
using System.Text;
using FluentAssertions;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Providers.ViaCep;

namespace PropertyIntelligence.Tests.Unit;

/// <summary>
/// T060 — Unit tests for the ViaCEP-backed address normalizer.
/// Uses a stub HttpMessageHandler to avoid real network calls.
/// Covers: successful normalization, 404 (unresolvable address), "erro":true response.
/// </summary>
public sealed class AddressNormalizerTests
{
    private static readonly PropertyAddress SaoPauloAddress = new()
    {
        NormalizedAddress = "Rua Augusta, 1500 - Consolação, São Paulo - SP",
        StreetName = "Rua Augusta",
        StreetNumber = "1500",
        Neighborhood = "Consolação",
        City = "São Paulo",
        State = "SP",
        PostalCode = "01310100",
    };

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_WithValidCep_ReturnsNormalizedAddressInfo()
    {
        const string validJson = """
            {
                "cep": "01310-100",
                "logradouro": "Rua Augusta",
                "complemento": "",
                "bairro": "Consolação",
                "localidade": "São Paulo",
                "uf": "SP",
                "ibge": "3550308",
                "erro": false
            }
            """;

        using var httpClient = BuildHttpClient(HttpStatusCode.OK, validJson);
        var provider = new ViaCepAddressProvider(httpClient, TimeSpan.FromDays(30));

        var result = await provider.FetchAsync(SaoPauloAddress);

        result.Should().NotBeNull();
        result.Data.City.Should().Be("São Paulo");
        result.Data.State.Should().Be("SP");
        result.Data.StreetName.Should().Be("Rua Augusta");
        result.Data.Neighborhood.Should().Be("Consolação");
        result.Data.NormalizedAddress.Should().Contain("Rua Augusta");
        result.Data.NormalizedAddress.Should().Contain("São Paulo - SP");
        result.RawPayload.Should().Be(validJson, "raw payload must be preserved for auditability");
    }

    [Fact]
    public async Task FetchAsync_WithValidCep_RetainsStreetNumber()
    {
        const string validJson = """
            {
                "cep": "01310-100",
                "logradouro": "Rua Augusta",
                "bairro": "Consolação",
                "localidade": "São Paulo",
                "uf": "SP",
                "erro": false
            }
            """;

        using var httpClient = BuildHttpClient(HttpStatusCode.OK, validJson);
        var provider = new ViaCepAddressProvider(httpClient, TimeSpan.FromDays(30));

        var result = await provider.FetchAsync(SaoPauloAddress);

        result.Data.StreetNumber.Should().Be("1500");
        result.Data.NormalizedAddress.Should().Contain("1500");
    }

    // ── 404 — address not found ───────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_WhenViaCepReturns404_ThrowsHttpRequestException()
    {
        using var httpClient = BuildHttpClient(HttpStatusCode.NotFound, "Not Found");
        var provider = new ViaCepAddressProvider(httpClient, TimeSpan.FromDays(30));

        // The caller (endpoint) catches this and returns HTTP 422
        var act = () => provider.FetchAsync(SaoPauloAddress);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── "erro": true — invalid/unresolvable CEP ───────────────────────────────

    [Fact]
    public async Task FetchAsync_WhenViaCepRespondsWithErroTrue_ThrowsInvalidOperationException()
    {
        const string erroJson = """{"erro": true}""";

        using var httpClient = BuildHttpClient(HttpStatusCode.OK, erroJson);
        var provider = new ViaCepAddressProvider(httpClient, TimeSpan.FromDays(30));

        var act = () => provider.FetchAsync(SaoPauloAddress);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ── Municipality/state mismatch ───────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_WhenViaCepLocalidadeMismatchesRequestedCity_ThrowsInvalidOperationException()
    {
        const string mismatchJson = """
            {
                "cep": "01310-100",
                "logradouro": "Rua Augusta",
                "bairro": "Centro",
                "localidade": "Campinas",
                "uf": "SP",
                "erro": false
            }
            """;

        using var httpClient = BuildHttpClient(HttpStatusCode.OK, mismatchJson);
        var provider = new ViaCepAddressProvider(httpClient, TimeSpan.FromDays(30));

        var act = () => provider.FetchAsync(SaoPauloAddress);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*localidade*uf*");
    }

    // ── No PostalCode fallback ────────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_WhenNoPostalCode_ReturnsBestEffortAddressInfoWithoutHttpCall()
    {
        var addressWithoutCep = new PropertyAddress
        {
            NormalizedAddress = "Rua Augusta, 1500, São Paulo",
            StreetName = "Rua Augusta",
            StreetNumber = "1500",
            City = "São Paulo",
            State = "SP",
        };

        // Handler should never be invoked when PostalCode is null
        using var httpClient = BuildHttpClient(
            HttpStatusCode.InternalServerError,
            "should not be called");
        var provider = new ViaCepAddressProvider(httpClient, TimeSpan.FromDays(30));

        var result = await provider.FetchAsync(addressWithoutCep);

        result.Should().NotBeNull();
        result.Data.City.Should().Be("São Paulo");
        result.Data.State.Should().Be("SP");
        result.RawPayload.Should().Be("{}");
    }

    // ── Provider metadata ─────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_IsViacep()
    {
        using var httpClient = BuildHttpClient(HttpStatusCode.OK, "{}");
        var provider = new ViaCepAddressProvider(httpClient, TimeSpan.FromDays(30));

        provider.ProviderName.Should().Be("viacep");
    }

    [Fact]
    public void CacheTtl_ReflectsConfiguredValue()
    {
        var expected = TimeSpan.FromDays(30);
        using var httpClient = BuildHttpClient(HttpStatusCode.OK, "{}");
        var provider = new ViaCepAddressProvider(httpClient, expected);

        provider.CacheTtl.Should().Be(expected);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HttpClient BuildHttpClient(HttpStatusCode statusCode, string body)
    {
        var handler = new StubHttpMessageHandler(statusCode, body);
        return new HttpClient(handler) { BaseAddress = new Uri("https://viacep.com.br") };
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string body)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
