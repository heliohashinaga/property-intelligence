using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Registry;
using PropertyIntelligence.Providers.Registry;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace PropertyIntelligence.Tests.Integration;

/// <summary>
/// T061 / T062 / T082 — Integration tests for the full analysis pipeline.
///
/// Uses Testcontainers (PostgreSQL+PostGIS, Redis) so Docker is required at
/// test runtime. Run with:
///   dotnet test tests/PropertyIntelligence.Tests.Integration
/// </summary>
[Trait("Category", "Integration")]
public sealed class PropertyAnalysisIntegrationTests
    : IClassFixture<PropertyAnalysisIntegrationTests.IntegrationTestFactory>
{
    private const string ValidApiKey = "integration-test-api-key";
    private readonly IntegrationTestFactory _factory;

    public PropertyAnalysisIntegrationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    // ── T061: Full pipeline ───────────────────────────────────────────────────

    [Fact]
    public async Task PostAnalyze_WithAllMockProviders_Returns200AndPersistsAnalysis()
    {
        using var client = _factory.CreateClient();

        using var response = await SendAnalyzeAsync(client);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 OK but got {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // Contract shape
        root.GetProperty("address").GetProperty("normalized").GetString().Should().NotBeNullOrWhiteSpace();
        var composite = root.GetProperty("score").GetProperty("composite").GetInt32();
        composite.Should().BeGreaterThan(0);
        root.GetProperty("score").GetProperty("grade").GetString().Should().NotBeNullOrWhiteSpace();

        Guid.TryParse(root.GetProperty("analysis_id").GetString(), out var analysisId)
            .Should().BeTrue();
        DateTimeOffset.TryParse(root.GetProperty("analyzed_at").GetString(), out _)
            .Should().BeTrue();

        // DB persistence (T037): property_analyses row exists
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PropertyIntelligenceDbContext>();

        var analysis = await db.PropertyAnalyses.FindAsync(analysisId);
        analysis.Should().NotBeNull("analysis must be persisted to property_analyses");

        // data_provider_raw_logs rows created
        var rawLogCount = await db.DataProviderRawLogs
            .Where(l => l.AnalysisId == analysisId)
            .CountAsync();
        rawLogCount.Should().BeGreaterThan(0, "raw provider logs must be persisted");
    }

    [Fact]
    public async Task PostAnalyze_WithAllMockProviders_ResponseHasDimensionsAndFlags()
    {
        using var client = _factory.CreateClient();
        using var response = await SendAnalyzeAsync(client);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var dimensions = root.GetProperty("score").GetProperty("dimensions");
        foreach (var dim in new[] { "security", "mobility", "infrastructure", "environment", "appreciation", "urban_context" })
        {
            var d = dimensions.GetProperty(dim);
            d.GetProperty("status").GetString().Should().Be("available");
            d.GetProperty("score").ValueKind.Should().Be(JsonValueKind.Number);
            d.GetProperty("max").GetInt32().Should().Be(200);
        }

        root.GetProperty("risk_flags").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("opportunity_flags").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("warnings").GetArrayLength().Should().Be(0);
        root.GetProperty("providers_unavailable").GetArrayLength().Should().Be(0);
    }

    // ── T062: Graceful degradation ────────────────────────────────────────────

    [Fact]
    public async Task PostAnalyze_WhenSecurityProviderFails_Returns200WithReducedMaxAndWarning()
    {
        using var factory = new IntegrationTestFactory(_factory, failSecurityProvider: true);
        using var client = factory.CreateClient();

        using var response = await SendAnalyzeAsync(client);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Graceful degradation must return 200, got {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // score.max must reflect 1 fewer dimension (1000 - 200 = 800)
        root.GetProperty("score").GetProperty("max").GetInt32().Should().Be(800);

        // warnings must contain an entry for the failed provider
        var warnings = root.GetProperty("warnings");
        warnings.GetArrayLength().Should().Be(1);
        var warning = warnings.EnumerateArray().Single();
        warning.GetProperty("provider").GetString().Should().Be("mock_security");

        // providers_unavailable must list the failed provider
        var unavailable = root.GetProperty("providers_unavailable")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        unavailable.Should().Contain("mock_security");
    }

    // ── T082: Performance constraints ─────────────────────────────────────────

    [Fact]
    public async Task PostAnalyze_NonCachedRequest_CompletesWithinSc001Threshold()
    {
        // SC-001: non-cached analysis ≤ 8 s (mock providers, no network)
        using var client = _factory.CreateClient();

        var sw = Stopwatch.StartNew();
        using var response = await SendAnalyzeAsync(client);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(8_000,
            $"SC-001: non-cached request must complete within 8 s; took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task PostAnalyze_CachedRequest_CompletesWithinSc002Threshold()
    {
        // SC-002: cached analysis ≤ 500 ms (provider results in Redis)
        using var client = _factory.CreateClient();

        // First request warms the cache
        using var warmup = await SendAnalyzeAsync(client);
        warmup.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second request should be served from cache
        var sw = Stopwatch.StartNew();
        using var response = await SendAnalyzeAsync(client);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(500,
            $"SC-002: cached request must complete within 500 ms; took {sw.ElapsedMilliseconds} ms");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> SendAnalyzeAsync(HttpClient client)
        => client.PostAsJsonAsync(
            "/v1/property/analyze",
            new { address = "Rua Augusta, 1500, São Paulo" },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts Testcontainers (PostGIS + Redis), runs migrations, and boots the API
    /// with overridden connection strings so no external infrastructure is needed.
    /// </summary>
    public sealed class IntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres;
        private readonly RedisContainer _redis;
        private readonly bool _failSecurityProvider;

        // Called by the IClassFixture path — primary factory
        public IntegrationTestFactory()
            : this(null, false) { }

        // Called by T062 test to share the already-running containers
        internal IntegrationTestFactory(IntegrationTestFactory? parent, bool failSecurityProvider)
        {
            _failSecurityProvider = failSecurityProvider;

            if (parent is not null)
            {
                // Reuse parent containers — they are already started
                _postgres = parent._postgres;
                _redis = parent._redis;
            }
            else
            {
                _postgres = new PostgreSqlBuilder()
                    .WithImage("postgis/postgis:16-3.4")
                    .WithDatabase("property_intelligence")
                    .WithUsername("property_intelligence")
                    .WithPassword("property_intelligence")
                    .WithWaitStrategy(Wait.ForUnixContainer()
                        .UntilCommandIsCompleted("pg_isready", "-U", "property_intelligence"))
                    .Build();

                _redis = new RedisBuilder()
                    .WithImage("redis:7-alpine")
                    .Build();
            }
        }

        async Task IAsyncLifetime.InitializeAsync()
        {
            // Only start containers for the primary (non-child) factory
            if (_postgres.State != TestcontainersStates.Running)
            {
                await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
                await ApplyMigrationsAsync();
            }
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            await DisposeAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DATABASE_URL"] = BuildConnectionString(),
                    ["REDIS_URL"] = _redis.GetConnectionString(),
                    ["Providers:Profile"] = "mock",
                });
            });

            builder.ConfigureServices(services =>
            {
                // Inject a test API consumer so auth middleware passes
                services.AddSingleton<IStartupFilter>(new IntegrationApiConsumerStartupFilter(ValidApiKey));

                // Use the mock provider registry from the fixture file
                services.RemoveAll<IProviderRegistry>();
                var registryFixturePath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Migrations",
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "tests",
                    "PropertyIntelligence.Tests.Contract",
                    "Fixtures",
                    "Registry",
                    "mock-mvp.providers.json");

                // Fallback: use an inline mock registry if the fixture is not found
                IProviderRegistry registry;
                if (File.Exists(registryFixturePath))
                {
                    var descriptors = RegistryProviderRegistry.LoadEnabled(registryFixturePath);
                    registry = new ProviderRegistry(descriptors);
                }
                else
                {
                    registry = new ProviderRegistry(BuildInlineMockDescriptors());
                }

                services.AddSingleton(registry);

                if (_failSecurityProvider)
                {
                    services.RemoveAll<IDataProvider<CrimeData>>();
                    services.AddSingleton<IDataProvider<CrimeData>>(new FailingSecurityProvider());
                }
            });
        }

        private string BuildConnectionString()
        {
            var connStr = _postgres.GetConnectionString();
            // Ensure no SSL requirement for local Testcontainers
            if (!connStr.Contains("SslMode", StringComparison.OrdinalIgnoreCase))
            {
                connStr += ";SslMode=Disable;TrustServerCertificate=true";
            }

            return connStr;
        }

        private async Task ApplyMigrationsAsync()
        {
            // Ensure pgcrypto and PostGIS extensions exist before EnsureCreated
            var connStr = BuildConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var extensionSql = """
                CREATE EXTENSION IF NOT EXISTS "pgcrypto";
                CREATE EXTENSION IF NOT EXISTS "postgis";
                """;

            await using (var cmd = new NpgsqlCommand(extensionSql, conn))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            // Use EF Core EnsureCreated to create the remaining schema from the model
            var options = new DbContextOptionsBuilder<PropertyIntelligenceDbContext>()
                .UseNpgsql(connStr, npgsql => npgsql.UseNetTopologySuite())
                .Options;

            await using var db = new PropertyIntelligenceDbContext(options);
            await db.Database.EnsureCreatedAsync();
        }

        private static IReadOnlyList<ProviderDescriptor> BuildInlineMockDescriptors()
            =>
            [
                MockDescriptor("mock_address",   "address"),
                MockDescriptor("mock_security",  "security"),
                MockDescriptor("mock_mobility",  "mobility"),
                MockDescriptor("mock_environment", "environment"),
                MockDescriptor("mock_health",    "infrastructure"),
                MockDescriptor("mock_school",    "infrastructure"),
                MockDescriptor("mock_appreciation", "appreciation"),
                MockDescriptor("mock_urban_context", "urban_context"),
            ];

        private static ProviderDescriptor MockDescriptor(string id, string capability) => new()
        {
            ProviderId = id,
            DisplayName = id,
            Enabled = true,
            Capabilities = [capability],
            CacheTtl = TimeSpan.FromMinutes(5),
            Timeout = TimeSpan.FromSeconds(10),
            SourceType = SourceType.Imported,
            Version = "1.0.0",
        };
    }

    // ── Supporting types ──────────────────────────────────────────────────────

    private sealed class IntegrationApiConsumerStartupFilter(string validApiKey) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (ctx, nextMw) =>
                {
                    if (ctx.Request.Headers.TryGetValue("X-Api-Key", out var key)
                        && StringComparer.Ordinal.Equals(key.ToString(), validApiKey))
                    {
                        ctx.Items["ApiConsumer"] = new ApiConsumer
                        {
                            Name = "integration-tests",
                            ApiKeyHash = Convert.ToHexString(
                                SHA256.HashData(Encoding.UTF8.GetBytes(validApiKey))).ToLowerInvariant(),
                            IsActive = true,
                        };
                        ctx.Items["ClientIp"] = "127.0.0.1";
                    }

                    await nextMw(ctx);
                });

                next(app);
            };
        }
    }

    private static Task<HttpResponseMessage> SendAnalyzeAsync(
        HttpClient client,
        string address = "Rua Augusta, 1500, São Paulo")
    {
        // Add API key header
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/property/analyze")
        {
            Content = JsonContent.Create(new { address }),
        };
        request.Headers.Add("X-Api-Key", ValidApiKey);
        return client.SendAsync(request);
    }

    private sealed class FailingSecurityProvider : IDataProvider<CrimeData>
    {
        public string ProviderName => "mock_security";
        public TimeSpan CacheTtl => TimeSpan.FromMinutes(1);

        public Task<ProviderFetchResult<CrimeData>> FetchAsync(
            PropertyAddress address,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Simulating SSP/crime provider failure (T062)");
    }
}

// Inline fixture loader (mirrors Tests.Contract/Fixtures/RegistryProviderRegistry.cs)
file static class RegistryProviderRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<ProviderDescriptor> LoadEnabled(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            return [];
        }

        var json = File.ReadAllText(jsonPath);
        var entries = JsonSerializer.Deserialize<List<FixtureEntry>>(json, JsonOptions) ?? [];
        return entries.Where(e => e.Enabled).Select(Map).ToList();
    }

    private static ProviderDescriptor Map(FixtureEntry e) => new()
    {
        ProviderId = e.ProviderId,
        DisplayName = e.DisplayName,
        Enabled = e.Enabled,
        Capabilities = (e.Capabilities ?? []).ToList(),
        CacheTtl = TimeSpan.FromSeconds(e.CacheTtlSeconds),
        Timeout = TimeSpan.FromSeconds(e.TimeoutSeconds),
        SourceType = SourceType.Imported,
        Version = e.Version,
    };

    private sealed class FixtureEntry
    {
        public string ProviderId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        public string[]? Capabilities { get; init; }
        public int CacheTtlSeconds { get; init; }
        public int TimeoutSeconds { get; init; }
        public string Version { get; init; } = "1.0.0";
    }
}
