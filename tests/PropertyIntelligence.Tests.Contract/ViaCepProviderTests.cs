using PropertyIntelligence.Core.Domain;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace PropertyIntelligence.Tests.Contract;

/// <summary>
/// Contract test: ViaCepProvider parses ViaCEP API responses correctly.
/// RED PHASE — ViaCepProvider not implemented yet (T018).
/// </summary>
[Trait("Category", "Contract")]
public sealed class ViaCepProviderTests : IDisposable
{
    private readonly WireMockServer _stub;

    public ViaCepProviderTests()
    {
        _stub = WireMockServer.Start();

        // Stub: valid CEP lookup
        _stub.Given(Request.Create().WithPath("/ws/01310100/json/").UsingGet())
             .RespondWith(Response.Create()
                 .WithStatusCode(200)
                 .WithBodyAsJson(new
                 {
                     cep = "01310-100",
                     logradouro = "Rua Augusta",
                     complemento = "",
                     bairro = "Consolação",
                     localidade = "São Paulo",
                     uf = "SP",
                     ibge = "3550308",
                     gia = "",
                     ddd = "11",
                     siafi = "7107"
                 }));

        // Stub: CEP not found
        _stub.Given(Request.Create().WithPath("/ws/00000000/json/").UsingGet())
             .RespondWith(Response.Create()
                 .WithStatusCode(200)
                 .WithBodyAsJson(new { erro = true }));

        // Stub: Nominatim geocode
        _stub.Given(Request.Create().WithPath("/search").UsingGet())
             .RespondWith(Response.Create()
                 .WithStatusCode(200)
                 .WithBodyAsJson(new[]
                 {
                     new { lat = "-23.5563", lon = "-46.6543", display_name = "Rua Augusta, São Paulo" }
                 }));
    }

    public void Dispose() => _stub.Stop();

    [Fact(Skip = "Red phase — ViaCepProvider not implemented yet (T018)")]
    public async Task FetchAsync_ValidCep_ReturnsPopulatedAddressInfo()
    {
        // Arrange
        // var provider = new ViaCepProvider(
        //     httpClientFactory: ...,   // configured with _stub.Urls[0] as base
        //     cacheService: ...,
        //     logger: ...);
        var address = new PropertyAddress
        {
            NormalizedAddress = "Rua Augusta, 1500, São Paulo, SP",
            PostalCode = "01310100",
            City = "São Paulo",
            State = "SP"
        };

        // Act
        // var result = await provider.FetchAsync(address);

        // Assert — provider must populate all fields from ViaCEP response
        // result.ShouldNotBeNull();
        // result.StreetName.ShouldBe("Rua Augusta");
        // result.Neighborhood.ShouldBe("Consolação");
        // result.City.ShouldBe("São Paulo");
        // result.State.ShouldBe("SP");
        // result.PostalCode.ShouldBe("01310100");
        // result.Lat.ShouldNotBeNull();
        // result.Lng.ShouldNotBeNull();

        // Temporary assertion so test body is not empty
        _stub.LogEntries.ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Fact(Skip = "Red phase — ViaCepProvider not implemented yet (T018)")]
    public async Task FetchAsync_InvalidCep_ReturnsNullOrMinimalAddress()
    {
        // When ViaCEP returns {erro: true}, provider should return null or throw
        // — the enrichment module handles it as ProvidersUnavailable
        var address = new PropertyAddress
        {
            NormalizedAddress = "Endereço Inválido",
            PostalCode = "00000000",
            City = "São Paulo",
            State = "SP"
        };

        // var result = await provider.FetchAsync(address);
        // result.ShouldBeNull();

        _stub.LogEntries.ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Fact(Skip = "Red phase — ViaCepProvider not implemented yet (T018)")]
    public async Task FetchAsync_ValidCep_CachesTtl30Days()
    {
        // provider.CacheTtl.ShouldBe(TimeSpan.FromDays(30));
        // provider.ProviderName.ShouldBe("viacep");
        await Task.CompletedTask;
    }
}
