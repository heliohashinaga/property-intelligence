using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using PropertyIntelligence.Api.Services;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using Shouldly;

namespace PropertyIntelligence.Tests.Unit;

/// <summary>
/// T060 — Unit tests for AddressNormalizerService (FR-009).
/// Covers all 5 scenarios:
/// (a) ViaCEP success → normalized address returned
/// (b) ViaCEP 404 → Nominatim fallback → success
/// (c) ViaCEP timeout → Nominatim fallback → success
/// (d) Both ViaCEP and Nominatim fail → null → caller returns HTTP 422 address_normalization_failed
/// (e) Null/empty input → null immediately
/// </summary>
[Trait("Category", "Unit")]
public sealed class AddressNormalizerTests
{
    private const string ValidCep = "01310100";

    // ── (a) ViaCEP success ────────────────────────────────────────────────────

    [Fact]
    public async Task NormalizeAsync_ValidCepAddress_ReturnsNormalizedAddress()
    {
        var viaCepMock  = BuildViaCepMock(returnAddress: SampleAddress());
        var httpFactory = BuildNominatimFactory(returnValidResult: false); // not reached
        var normalizer  = BuildNormalizer(viaCepMock.Object, httpFactory);

        var result = await normalizer.NormalizeAsync($"Rua Augusta, 1500, {ValidCep}");

        result.ShouldNotBeNull();
        result!.NormalizedAddress.ShouldNotBeNullOrWhiteSpace();
        result.Lat.ShouldNotBe(0);
        result.Lng.ShouldNotBe(0);
    }

    // ── (b) ViaCEP 404 → Nominatim fallback ──────────────────────────────────

    [Fact]
    public async Task NormalizeAsync_ViaCepReturns404_FallsBackToNominatimAndSucceeds()
    {
        var viaCepMock  = BuildViaCepMock(throws: new HttpRequestException("Not found", null, HttpStatusCode.NotFound));
        var httpFactory = BuildNominatimFactory(returnValidResult: true);
        var normalizer  = BuildNormalizer(viaCepMock.Object, httpFactory);

        var result = await normalizer.NormalizeAsync("Rua Augusta, 1500, São Paulo");

        result.ShouldNotBeNull();
    }

    // ── (c) ViaCEP timeout → Nominatim fallback ───────────────────────────────

    [Fact]
    public async Task NormalizeAsync_ViaCepTimeout_FallsBackToNominatimAndSucceeds()
    {
        var viaCepMock  = BuildViaCepMock(throws: new TaskCanceledException("ViaCEP timed out"));
        var httpFactory = BuildNominatimFactory(returnValidResult: true);
        var normalizer  = BuildNormalizer(viaCepMock.Object, httpFactory);

        var result = await normalizer.NormalizeAsync("Rua Augusta 1500 São Paulo");

        result.ShouldNotBeNull();
    }

    // ── (d) Both fail → null (caller maps to HTTP 422 address_normalization_failed) ──

    [Fact]
    public async Task NormalizeAsync_BothViaCepAndNominatimFail_ReturnsNull()
    {
        var viaCepMock  = BuildViaCepMock(throws: new HttpRequestException("ViaCEP unavailable"));
        var httpFactory = BuildNominatimFactory(returnValidResult: false, throwException: true);
        var normalizer  = BuildNormalizer(viaCepMock.Object, httpFactory);

        var result = await normalizer.NormalizeAsync("Rua Augusta 1500 São Paulo");

        // Null → AnalyzeEndpoint returns HTTP 422 "address_normalization_failed"
        result.ShouldBeNull();
    }

    // ── (e) Null/empty input → null ───────────────────────────────────────────

    [Fact]
    public async Task NormalizeAsync_NullOrWhitespaceInput_ReturnsNull()
    {
        var viaCepMock  = BuildViaCepMock(returnAddress: null);
        var httpFactory = BuildNominatimFactory(returnValidResult: false);
        var normalizer  = BuildNormalizer(viaCepMock.Object, httpFactory);

        var resultNull  = await normalizer.NormalizeAsync(null!);
        var resultEmpty = await normalizer.NormalizeAsync("   ");

        resultNull.ShouldBeNull();
        resultEmpty.ShouldBeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AddressNormalizerService BuildNormalizer(
        IDataProvider<PropertyAddress> viaCep,
        IHttpClientFactory             httpFactory) =>
        new(viaCep, httpFactory, NullLogger<AddressNormalizerService>.Instance);

    private static Mock<IDataProvider<PropertyAddress>> BuildViaCepMock(
        PropertyAddress? returnAddress = null,
        Exception?       throws        = null)
    {
        var mock = new Mock<IDataProvider<PropertyAddress>>();
        if (throws is not null)
            mock.Setup(p => p.FetchAsync(It.IsAny<PropertyAddress>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(throws);
        else
            mock.Setup(p => p.FetchAsync(It.IsAny<PropertyAddress>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(returnAddress);
        return mock;
    }

    private static IHttpClientFactory BuildNominatimFactory(
        bool returnValidResult,
        bool throwException = false)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var factoryMock = new Mock<IHttpClientFactory>();

        if (throwException)
        {
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Nominatim unavailable"));
        }
        else if (returnValidResult)
        {
            var json = JsonSerializer.Serialize(new[]
            {
                new
                {
                    lat          = "-23.5563",
                    lon          = "-46.6543",
                    display_name = "Rua Augusta, 1500, Consolação, São Paulo, SP",
                    address      = new { road = "Rua Augusta", house_number = "1500",
                                        suburb = "Consolação", city = "São Paulo",
                                        state = "SP", postcode = "01310-100" },
                },
            });

            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content    = new StringContent(json),
                });
        }
        else
        {
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content    = new StringContent("[]"),
                });
        }

        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handlerMock.Object)
            {
                BaseAddress = new Uri("https://nominatim.openstreetmap.org"),
            });

        return factoryMock.Object;
    }

    private static PropertyAddress SampleAddress() => new()
    {
        NormalizedAddress = "Rua Augusta, 1500 - Consolação, São Paulo - SP",
        StreetName        = "Rua Augusta",
        StreetNumber      = "1500",
        Neighborhood      = "Consolação",
        City              = "São Paulo",
        State             = "SP",
        PostalCode        = ValidCep,
        Lat               = -23.5563,
        Lng               = -46.6543,
    };
}
