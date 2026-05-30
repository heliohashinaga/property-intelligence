using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PropertyIntelligence.Api.Middleware;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace PropertyIntelligence.Tests.Contract;

/// <summary>
/// Contract test: POST /v1/property/analyze happy path.
/// RED PHASE — endpoint not yet implemented (T036). Tests are skipped in CI.
/// Remove Skip attribute after T036 is complete.
/// </summary>
[Trait("Category", "Contract")]
public sealed class AnalyzeEndpointTests : IDisposable
{
    private readonly WireMockServer _stub;

    public AnalyzeEndpointTests()
    {
        _stub = WireMockServer.Start();
        SetupAllProviderStubs();
    }

    public void Dispose() => _stub.Stop();

    // ── Stubs ────────────────────────────────────────────────────────────────

    private void SetupAllProviderStubs()
    {
        // ViaCEP — address lookup by CEP
        _stub.Given(Request.Create().WithPath("/ws/01310100/json/").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
             {
                 cep = "01310-100", logradouro = "Rua Augusta", complemento = "",
                 bairro = "Consolação", localidade = "São Paulo", uf = "SP",
                 ibge = "3550308", ddd = "11"
             }));

        // Nominatim — geocoding
        _stub.Given(Request.Create().WithPath("/search").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new[]
             {
                 new { lat = "-23.5563", lon = "-46.6543", display_name = "Rua Augusta, 1500, Consolação, São Paulo, SP" }
             }));

        // Overpass — POI query
        _stub.Given(Request.Create().WithPath("/api/interpreter").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
             {
                 version = 0.6, generator = "Overpass API",
                 elements = new object[]
                 {
                     new { type = "node", id = 1, lat = -23.555, lon = -46.654, tags = new Dictionary<string,string> { ["railway"] = "station", ["station"] = "subway" } },
                     new { type = "node", id = 2, lat = -23.556, lon = -46.655, tags = new Dictionary<string,string> { ["highway"] = "bus_stop" } },
                     new { type = "node", id = 3, lat = -23.558, lon = -46.657, tags = new Dictionary<string,string> { ["amenity"] = "hospital" } }
                 }
             }));

        // IPTU API
        _stub.Given(Request.Create().WithPath("/api/property").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
             {
                 valor_venal = 850000.00, zoneamento = "ZM-3a", historical = new[] { new { year = 2023, valor_venal = 800000.00 } }
             }));
    }

    // ── Tests (RED phase — skip until T036) ──────────────────────────────────

    [Fact(Skip = "Red phase — POST /v1/property/analyze endpoint not implemented yet (T036)")]
    public async Task PostAnalyze_ValidAddress_Returns200WithAllDimensions()
    {
        // Arrange
        var factory = new WebApplicationFactory<ApiKeyAuthMiddleware>()
            .WithWebHostBuilder(host =>
            {
                host.UseSetting("VIACEP_BASE_URL", _stub.Urls[0]);
                host.UseSetting("OVERPASS_BASE_URL", _stub.Urls[0]);
                host.UseSetting("IPTU_API_BASE_URL", _stub.Urls[0]);
                host.UseSetting("NOMINATIM_BASE_URL", _stub.Urls[0]);
                host.UseSetting("API_KEY_SALT", "test-salt-32-chars-placeholder-ok");
            });

        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/v1/property/analyze",
            new { address = "Rua Augusta, 1500, São Paulo" },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // score block
        var score = body.GetProperty("score");
        score.GetProperty("composite").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        score.GetProperty("max").GetInt32().ShouldBeGreaterThan(0);
        score.GetProperty("grade").GetString().ShouldNotBeNullOrWhiteSpace();

        // all 6 dimensions must be present
        var dims = score.GetProperty("dimensions");
        foreach (var key in new[] { "security", "mobility", "infrastructure", "environment", "appreciation", "urban_context" })
        {
            dims.TryGetProperty(key, out _).ShouldBeTrue($"dimension '{key}' missing from response");
        }

        // providers_used non-empty
        body.GetProperty("providers_used").GetArrayLength().ShouldBeGreaterThan(0);

        // analyzed_at is ISO-8601
        var analyzedAt = body.GetProperty("analyzed_at").GetString();
        DateTimeOffset.TryParse(analyzedAt, out _).ShouldBeTrue("analyzed_at must be valid ISO-8601");

        factory.Dispose();
    }

    [Fact(Skip = "Red phase — endpoint not implemented yet (T036)")]
    public async Task PostAnalyze_MissingApiKey_Returns401()
    {
        var factory = new WebApplicationFactory<ApiKeyAuthMiddleware>()
            .WithWebHostBuilder(host => host.UseSetting("API_KEY_SALT", "test-salt-32-chars-placeholder-ok"));

        var client = factory.CreateClient();
        // No X-Api-Key header

        var response = await client.PostAsJsonAsync(
            "/v1/property/analyze",
            new { address = "Rua Augusta, 1500, São Paulo" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        factory.Dispose();
    }

    [Fact(Skip = "Red phase — endpoint not implemented yet (T036)")]
    public async Task PostAnalyze_EmptyAddress_Returns400()
    {
        var factory = new WebApplicationFactory<ApiKeyAuthMiddleware>()
            .WithWebHostBuilder(host => host.UseSetting("API_KEY_SALT", "test-salt-32-chars-placeholder-ok"));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "any-key");

        var response = await client.PostAsJsonAsync(
            "/v1/property/analyze",
            new { address = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        factory.Dispose();
    }
}
