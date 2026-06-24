using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Registry;
using PropertyIntelligence.Tests.Contract.Fixtures;

namespace PropertyIntelligence.Tests.Contract;

public sealed class AnalyzeEndpointTests : IClassFixture<AnalyzeEndpointTests.AnalyzeEndpointTestFactory>
{
    private const string ValidApiKey = "contract-test-api-key";
    private const string HappyPathRegistryFixture = "mock-mvp.providers.json";
    private readonly AnalyzeEndpointTestFactory _factory;

    public AnalyzeEndpointTests(AnalyzeEndpointTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostAnalyze_WithMockRegistryEnabledProviders_ReturnsHappyPathContract()
    {
        var expectedProviderIds = LoadEnabledProviderIds(HappyPathRegistryFixture);

        using var client = _factory.CreateClient();
        using var response = await SendAnalyzeRequestAsync(client);
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body: {responseBody}");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return;
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        root.GetProperty("address").ShouldMatchAddressContract();
        root.GetProperty("score").ShouldMatchFullScoreContract();

        root.GetProperty("risk_flags").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("opportunity_flags").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("insight").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("insight_unavailable").GetBoolean().Should().BeFalse();
        root.GetProperty("warnings").GetArrayLength().Should().Be(0);
        root.GetProperty("providers_unavailable").GetArrayLength().Should().Be(0);
        var cachedValueKind = root.GetProperty("cached").ValueKind;
        (cachedValueKind is JsonValueKind.True or JsonValueKind.False).Should().BeTrue();

        var providersUsed = root.GetProperty("providers_used")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        providersUsed.Should().BeEquivalentTo(expectedProviderIds);

        Guid.TryParse(root.GetProperty("analysis_id").GetString(), out _).Should().BeTrue();
        DateTimeOffset.TryParse(root.GetProperty("analyzed_at").GetString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task PostAnalyze_WhenEnabledMockProviderFails_ReturnsPartialContract()
    {
        var expectedProviderIds = LoadEnabledProviderIds(HappyPathRegistryFixture)
            .Where(static providerId => providerId != "mock_security")
            .ToArray();

        using var factory = new AnalyzeEndpointTestFactory(HappyPathRegistryFixture, true);
        using var client = factory.CreateClient();
        using var response = await SendAnalyzeRequestAsync(client);
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body: {responseBody}");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return;
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        root.GetProperty("address").ShouldMatchAddressContract();

        var score = root.GetProperty("score");
        score.GetProperty("composite").ValueKind.Should().Be(JsonValueKind.Number);
        score.GetProperty("max").GetInt32().Should().Be(800);
        score.GetRequiredString("grade");

        var dimensions = score.GetProperty("dimensions");
        dimensions.GetProperty("security").ShouldMatchUnavailableDimensionContract();
        dimensions.GetProperty("mobility").ShouldMatchAvailableDimensionContract();
        dimensions.GetProperty("infrastructure").ShouldMatchAvailableDimensionContract();
        dimensions.GetProperty("environment").ShouldMatchAvailableDimensionContract();
        dimensions.GetProperty("appreciation").ShouldMatchAvailableDimensionContract();
        dimensions.GetProperty("urban_context").ShouldMatchAvailableDimensionContract();

        root.GetProperty("risk_flags").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("opportunity_flags").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("insight").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("insight_unavailable").GetBoolean().Should().BeFalse();

        var warnings = root.GetProperty("warnings");
        warnings.ValueKind.Should().Be(JsonValueKind.Array);
        warnings.GetArrayLength().Should().BeGreaterThan(0);
        var warning = warnings.EnumerateArray().Single();
        warning.GetRequiredString("dimension").Should().Be("security");
        warning.GetRequiredString("provider").Should().Be("mock_security");
        warning.GetRequiredString("message");

        var providersUnavailable = root.GetProperty("providers_unavailable")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        providersUnavailable.Should().Equal("mock_security");

        var cachedValueKind = root.GetProperty("cached").ValueKind;
        (cachedValueKind is JsonValueKind.True or JsonValueKind.False).Should().BeTrue();

        var providersUsed = root.GetProperty("providers_used")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        providersUsed.Should().BeEquivalentTo(expectedProviderIds);
        providersUsed.Should().NotContain("mock_security");

        Guid.TryParse(root.GetProperty("analysis_id").GetString(), out _).Should().BeTrue();
        DateTimeOffset.TryParse(root.GetProperty("analyzed_at").GetString(), out _).Should().BeTrue();
    }

    private static string[] LoadEnabledProviderIds(string registryFixtureFileName)
    {
        var registryFixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Registry",
            registryFixtureFileName);

        return RegistryProviderRegistry.LoadEnabled(registryFixturePath)
            .Select(descriptor => descriptor.ProviderId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<HttpResponseMessage> SendAnalyzeRequestAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/property/analyze")
        {
            Content = JsonContent.Create(new { address = "Rua Augusta, 1500, São Paulo" }),
        };
        request.Headers.Add("X-Api-Key", ValidApiKey);
        return await client.SendAsync(request);
    }

    public sealed class AnalyzeEndpointTestFactory : WebApplicationFactory<Program>
    {
        private readonly string _registryFixtureFileName;
        private readonly bool _failSecurityProvider;

        public AnalyzeEndpointTestFactory()
            : this(HappyPathRegistryFixture, false)
        {
        }

        internal AnalyzeEndpointTestFactory(
            string registryFixtureFileName,
            bool failSecurityProvider)
        {
            _registryFixtureFileName = registryFixtureFileName;
            _failSecurityProvider = failSecurityProvider;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Providers:Profile"] = "mock",
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProviderRegistry>();
                services.AddSingleton<IStartupFilter>(new TestApiConsumerStartupFilter(ValidApiKey));

                var registryFixturePath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "Registry",
                    _registryFixtureFileName);

                services.AddSingleton<IProviderRegistry>(_ =>
                    new ProviderRegistry(RegistryProviderRegistry.LoadEnabled(registryFixturePath)));

                if (_failSecurityProvider)
                {
                    services.RemoveAll<IDataProvider<CrimeData>>();
                    services.AddSingleton<IDataProvider<CrimeData>, FailingSecurityProvider>();
                }
            });
        }
    }
}

file sealed class TestApiConsumerStartupFilter(string validApiKey) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey)
                    && StringComparer.Ordinal.Equals(providedKey.ToString(), validApiKey))
                {
                    context.Items["ApiConsumer"] = new ApiConsumer
                    {
                        Name = "contract-tests",
                        ApiKeyHash = ComputeSha256Hex(validApiKey),
                        IsActive = true,
                    };
                    context.Items["ClientIp"] = "127.0.0.1";
                }

                await nextMiddleware(context);
            });

            next(app);
        };
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

file sealed class FailingSecurityProvider : IDataProvider<CrimeData>
{
    public string ProviderName => "mock_security";
    public TimeSpan CacheTtl => TimeSpan.FromDays(1);

    public Task<ProviderFetchResult<CrimeData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated mock_security failure for graceful-degradation contract test.");
}

file static class AnalyzeEndpointContractAssertions
{
    public static JsonElement ShouldMatchAddressContract(this JsonElement address)
    {
        address.ValueKind.Should().Be(JsonValueKind.Object);
        address.GetRequiredString("normalized");
        address.GetRequiredString("street");
        address.GetRequiredString("number");
        address.GetRequiredString("neighborhood");
        address.GetRequiredString("city");
        address.GetRequiredString("state");
        address.GetRequiredString("postal_code");

        var coordinates = address.GetProperty("coordinates");
        coordinates.ValueKind.Should().Be(JsonValueKind.Object);
        coordinates.GetProperty("latitude").ValueKind.Should().Be(JsonValueKind.Number);
        coordinates.GetProperty("longitude").ValueKind.Should().Be(JsonValueKind.Number);

        return address;
    }

    public static JsonElement ShouldMatchFullScoreContract(this JsonElement score)
    {
        score.ValueKind.Should().Be(JsonValueKind.Object);
        score.GetProperty("composite").ValueKind.Should().Be(JsonValueKind.Number);
        score.GetProperty("max").ValueKind.Should().Be(JsonValueKind.Number);
        score.GetProperty("max").GetInt32().Should().Be(1000);
        score.GetRequiredString("grade");

        var dimensions = score.GetProperty("dimensions");
        dimensions.ValueKind.Should().Be(JsonValueKind.Object);

        foreach (var dimensionName in new[]
                 {
                     "security",
                     "mobility",
                     "infrastructure",
                     "environment",
                     "appreciation",
                     "urban_context",
                 })
        {
            dimensions.GetProperty(dimensionName).ShouldMatchAvailableDimensionContract();
        }

        return score;
    }

    public static JsonElement ShouldMatchAvailableDimensionContract(this JsonElement dimension)
    {
        dimension.ValueKind.Should().Be(JsonValueKind.Object);
        dimension.GetProperty("score").ValueKind.Should().Be(JsonValueKind.Number);
        dimension.GetProperty("max").ValueKind.Should().Be(JsonValueKind.Number);
        dimension.GetRequiredString("trend");
        dimension.GetRequiredString("status").Should().Be("available");
        return dimension;
    }

    public static JsonElement ShouldMatchUnavailableDimensionContract(this JsonElement dimension)
    {
        dimension.ValueKind.Should().Be(JsonValueKind.Object);
        dimension.GetProperty("score").ValueKind.Should().Be(JsonValueKind.Null);
        dimension.GetProperty("max").ValueKind.Should().Be(JsonValueKind.Number);
        dimension.GetProperty("trend").ValueKind.Should().Be(JsonValueKind.Null);
        dimension.GetRequiredString("status").Should().Be("unavailable");
        return dimension;
    }

    public static string GetRequiredString(this JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        value.Should().NotBeNullOrWhiteSpace();
        return value!;
    }
}
