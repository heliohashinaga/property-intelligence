using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Explainability;

namespace PropertyIntelligence.Tests.Unit;

public sealed class LlmExplainabilityServiceTests
{
    [Fact]
    public async Task GenerateInsightAsync_returns_text_and_sends_expected_openrouter_request()
    {
        using var env = new EnvironmentScope(
            ("OPENROUTER_API_KEY", "test-openrouter-key"),
            ("LLM_MODEL", "meta-llama/llama-3.1-8b-instruct:free"));

        string? capturedUri = null;
        AuthenticationHeaderValue? capturedAuthorization = null;
        string? capturedReferer = null;
        string? capturedTitle = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            capturedUri = request.RequestUri?.ToString();
            capturedAuthorization = request.Headers.Authorization;
            capturedReferer = request.Headers.TryGetValues("HTTP-Referer", out var refererValues)
                ? refererValues.SingleOrDefault()
                : null;
            capturedTitle = request.Headers.TryGetValues("X-Title", out var titleValues)
                ? titleValues.SingleOrDefault()
                : null;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                content = "Insight de teste em PT-BR.",
                            },
                        },
                    },
                }),
            };

            return response;
        });

        using var httpClient = new HttpClient(handler);
        var service = new LlmExplainabilityService(httpClient, NullLogger<LlmExplainabilityService>.Instance);

        var profile = BuildProfile();
        var analysis = BuildAnalysis(profile.Address.Id, providersUnavailable: ["mock_security"]);

        var insight = await service.GenerateInsightAsync(analysis, profile);

        insight.Should().Be("Insight de teste em PT-BR.");

        capturedUri.Should().Be("https://openrouter.ai/api/v1/chat/completions");
        capturedAuthorization.Should().NotBeNull();
        capturedAuthorization!.Scheme.Should().Be("Bearer");
        capturedAuthorization.Parameter.Should().Be("test-openrouter-key");
        capturedReferer.Should().Be("https://property-intelligence.hashinaga.dev");
        capturedTitle.Should().Be("Property Intelligence");

        var requestBody = JsonDocument.Parse(capturedBody!);
        requestBody.RootElement.GetProperty("model").GetString().Should().Be("meta-llama/llama-3.1-8b-instruct:free");
        requestBody.RootElement.GetProperty("route").GetString().Should().Be("fallback");
        requestBody.RootElement.GetProperty("models").EnumerateArray().Select(e => e.GetString()).Should().Contain("meta-llama/llama-3.1-8b-instruct:free");

        var userMessage = requestBody.RootElement.GetProperty("messages").EnumerateArray()
            .First(e => e.GetProperty("role").GetString() == "user")
            .GetProperty("content")
            .GetString();

        userMessage.Should().Contain("Rua Augusta, 1500");
        userMessage.Should().Contain("724/1000");
        userMessage.Should().Contain("mock_security");
    }

    [Fact]
    public async Task GenerateInsightAsync_returns_null_on_timeout()
    {
        using var env = new EnvironmentScope(("OPENROUTER_API_KEY", "test-openrouter-key"));

        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Expected cancellation before completion");
        });

        using var httpClient = new HttpClient(handler);
        var service = new LlmExplainabilityService(
            httpClient,
            NullLogger<LlmExplainabilityService>.Instance,
            requestTimeout: TimeSpan.FromMilliseconds(30));

        var profile = BuildProfile();
        var analysis = BuildAnalysis(profile.Address.Id);

        var sw = Stopwatch.StartNew();
        var insight = await service.GenerateInsightAsync(analysis, profile);
        sw.Stop();

        insight.Should().BeNull();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GenerateInsightAsync_returns_null_when_openrouter_returns_non_success_status()
    {
        using var env = new EnvironmentScope(("OPENROUTER_API_KEY", "test-openrouter-key"));

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = JsonContent.Create(new { error = "upstream failed" }),
            };

            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var service = new LlmExplainabilityService(httpClient, NullLogger<LlmExplainabilityService>.Instance);

        var profile = BuildProfile();
        var analysis = BuildAnalysis(profile.Address.Id);

        var insight = await service.GenerateInsightAsync(analysis, profile);

        insight.Should().BeNull();
    }

    [Fact]
    public async Task GenerateInsightAsync_returns_null_when_response_payload_is_invalid()
    {
        using var env = new EnvironmentScope(("OPENROUTER_API_KEY", "test-openrouter-key"));

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[]}"),
            };

            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var service = new LlmExplainabilityService(httpClient, NullLogger<LlmExplainabilityService>.Instance);

        var profile = BuildProfile();
        var analysis = BuildAnalysis(profile.Address.Id);

        var insight = await service.GenerateInsightAsync(analysis, profile);

        insight.Should().BeNull();
    }

    private static PropertyProfile BuildProfile()
    {
        var address = new PropertyAddress
        {
            Id = Guid.NewGuid(),
            NormalizedAddress = "Rua Augusta, 1500 - Consolação, São Paulo - SP",
            StreetName = "Rua Augusta",
            StreetNumber = "1500",
            Neighborhood = "Consolação",
            City = "São Paulo",
            State = "SP",
            PostalCode = "01310100",
            Lat = -23.5563,
            Lng = -46.6543,
        };

        return new PropertyProfile
        {
            Address = address,
            AddressInfo = new AddressInfo
            {
                NormalizedAddress = address.NormalizedAddress,
                StreetName = address.StreetName,
                StreetNumber = address.StreetNumber,
                Neighborhood = address.Neighborhood,
                City = address.City,
                State = address.State,
                PostalCode = address.PostalCode,
                Lat = address.Lat,
                Lng = address.Lng,
            },
            CrimeData = new CrimeData
            {
                CrimeRatePer100k = 95,
                SecurityTrend = TrendDirection.Stable,
            },
            ProvidersUnavailable = [],
            AllFromCache = false,
        };
    }

    private static PropertyAnalysis BuildAnalysis(Guid addressId, IReadOnlyList<string>? providersUnavailable = null)
    {
        return new PropertyAnalysis
        {
            AddressId = addressId,
            ApiConsumerId = Guid.NewGuid(),
            CompositeScore = 724,
            CompositeMax = 1000,
            Grade = "B+",
            DimensionScores =
            [
                DimensionScore.Available("security", 118, TrendDirection.Stable),
                DimensionScore.Available("mobility", 185, TrendDirection.Improving),
                DimensionScore.Available("infrastructure", 162, TrendDirection.Stable),
                DimensionScore.Available("environment", 95, TrendDirection.Worsening),
                DimensionScore.Available("appreciation", 104, TrendDirection.Improving),
                DimensionScore.Available("urban_context", 60, TrendDirection.Stable),
            ],
            RiskFlags = ["moderate_flood_risk", "crime_trend_12m"],
            OpportunityFlags = ["metro_line6_nearby_2026", "zoning_upscale"],
            Insight = null,
            InsightUnavailable = true,
            LlmModel = "meta-llama/llama-3.1-8b-instruct:free",
            RulesVersion = "1.0.0",
            Warnings = [],
            ProvidersUsed = ["mock_address", "mock_mobility"],
            ProvidersUnavailable = providersUnavailable ?? [],
            Cached = false,
            RequestIp = "127.0.0.1",
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly List<(string Key, string? Original)> _originals = [];

        public EnvironmentScope(params (string Key, string? Value)[] assignments)
        {
            foreach (var (key, value) in assignments)
            {
                _originals.Add((key, Environment.GetEnvironmentVariable(key)));
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, original) in _originals)
            {
                Environment.SetEnvironmentVariable(key, original);
            }
        }
    }
}
