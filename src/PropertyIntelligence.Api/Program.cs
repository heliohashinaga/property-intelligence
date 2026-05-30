using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using NRules;
using Polly;
using NRules.Fluent;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PropertyIntelligence.Api.Endpoints;
using PropertyIntelligence.Api.Middleware;
using PropertyIntelligence.Api.Services;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Services;
using PropertyIntelligence.Explainability;
using PropertyIntelligence.Providers.Ana;
using PropertyIntelligence.Providers.Cnes;
using PropertyIntelligence.Dispatcher;
using PropertyIntelligence.Dispatcher.DomainEvents;
using PropertyIntelligence.Providers.Crime;
using PropertyIntelligence.Providers.Ibge;
using PropertyIntelligence.Providers.Inep;
using PropertyIntelligence.Providers.Iptu;
using PropertyIntelligence.Providers.Overpass;
using PropertyIntelligence.Providers.Shared;
using PropertyIntelligence.Providers.ViaCep;
using PropertyIntelligence.Rules;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Structured JSON logging — Constitution Principle V (T058) ────────────────
// Every request handler can inject ILogger<T>; output is structured JSON
// consumed by Grafana Cloud via OTLP or stdout log shipping.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(opts =>
{
    opts.IncludeScopes     = true;
    opts.TimestampFormat   = "O";            // ISO 8601 with offset
    opts.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});

// ── PostgreSQL / EF Core ──────────────────────────────────────────────────────
// Aspire injects ConnectionStrings__property-intelligence-db (Npgsql format).
// Fallback: DATABASE_URL env var (postgres://user:pass@host:port/db format).
var aspireConnStr = builder.Configuration.GetConnectionString("property-intelligence-db");
var databaseUrl   = aspireConnStr
    ?? builder.Configuration["DATABASE_URL"]
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=property_intelligence;Username=property_intelligence;Password=property_intelligence";

// Convert postgres:// URL → Npgsql connection string when not in Aspire mode
if (aspireConnStr == null && databaseUrl.StartsWith("postgres"))
{
    var uri  = new Uri(databaseUrl);
    var info = uri.UserInfo.Split(':');
    databaseUrl = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};" +
                  $"Username={info[0]};Password={info[1]}";
}

builder.Services.AddDbContext<PropertyIntelligenceDbContext>(opts =>
    opts.UseNpgsql(databaseUrl, npgsql =>
    {
        npgsql.UseNetTopologySuite();
        npgsql.EnableRetryOnFailure(maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);  // retries on transient Npgsql errors
    }),
    ServiceLifetime.Scoped);
builder.Services.AddDbContextFactory<PropertyIntelligenceDbContext>(opts =>
    opts.UseNpgsql(databaseUrl, npgsql =>
    {
        npgsql.UseNetTopologySuite();
        npgsql.EnableRetryOnFailure(maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    }),
    ServiceLifetime.Scoped);

// ── Redis ─────────────────────────────────────────────────────────────────────
// Aspire injects ConnectionStrings__redis (host:port format).
// Fallback: REDIS_URL env var (redis://host:port format).
var redisConnStr = builder.Configuration.GetConnectionString("redis")
    ?? (builder.Configuration["REDIS_URL"]
        ?? Environment.GetEnvironmentVariable("REDIS_URL")
        ?? "redis://localhost:6379")
       .Replace("redis://", "");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnStr));

// ── .NET 10 features ───────────────────────────────────────────────────────────
builder.Services.AddOpenApi();           // Native OpenAPI 3.1 document generation
builder.Services.AddValidation();        // Data Annotations validation on Minimal API DTOs
builder.Services.AddSingleton(TimeProvider.System); // Testable time abstraction

// ── Custom CQRS Dispatcher (MIT — no MediatR) (T083) ────────────────────────
// MediatR ≥13 is commercial (Lucky Penny Software, Jul/2025).
// FrozenDictionary-backed dispatcher: 4.4× faster, 8.3× less allocation than MediatR 12.
builder.Services.AddDispatcher(typeof(Program).Assembly);
builder.Services.AddSingleton<DomainEventChannel>();
builder.Services.AddHostedService<DomainEventProcessor>();

// ── Application services (T039) ──────────────────────────────────────────────
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddScoped<IAddressNormalizer, AddressNormalizerService>();

// Providers
builder.Services.AddScoped<IDataProvider<PropertyAddress>, ViaCepProvider>(); // used by AddressNormalizerService
builder.Services.AddScoped<IDataProvider<PoiData>, OverpassPoiProvider>();
builder.Services.AddScoped<IDataProvider<FloodRiskData>, AnaFloodRiskProvider>();
builder.Services.AddScoped<IDataProvider<CensusData>, IbgeCensusProvider>();
builder.Services.AddScoped<IDataProvider<CrimeData>, CrimeDataProvider>();
builder.Services.AddScoped<IDataProvider<HealthData>, CnesHealthProvider>();
builder.Services.AddScoped<IDataProvider<SchoolData>, InepSchoolProvider>();
builder.Services.AddScoped<IDataProvider<IptuData?>, IptuApiProvider>();
builder.Services.AddScoped<PropertyEnrichmentModule>();

// NRules engine (T027-T034)
var ruleRepository = new RuleRepository();
ruleRepository.Load(x => x.From(typeof(PropertyAnalysisEngine).Assembly));
builder.Services.AddSingleton<ISessionFactory>(ruleRepository.Compile());
builder.Services.AddScoped<IPropertyAnalysisEngine, PropertyAnalysisEngine>();

// LLM explainability (T035)
// ── HTTP Clients com resiliência (Polly v8 via Microsoft.Extensions.Http.Resilience) ──
// ── URLs de providers via appsettings.json (seção Providers) ─────────────────
// Sobrescreva em appsettings.Development.json, variáveis de ambiente ou
// em testes via WebApplicationFactory.WithWebHostBuilder para apontar ao WireMock.
var p = builder.Configuration.GetSection("Providers");
var urlViaCep    = p["ViaCep:BaseUrl"]     ?? "https://viacep.com.br";
var urlNominatim = p["Nominatim:BaseUrl"]  ?? "https://nominatim.openstreetmap.org";
var urlNominatimUA = p["Nominatim:UserAgent"] ?? "PropertyIntelligence/1.0 (contact@hashinaga.dev)";
var urlOverpass  = p["Overpass:BaseUrl"]   ?? "https://overpass-api.de";
var urlIptuApi   = p["IptuApi:BaseUrl"]    ?? "https://api.iptuapi.com.br";
var urlOpenRouter = p["OpenRouter:BaseUrl"] ?? "https://openrouter.ai";
var orReferer    = p["OpenRouter:HttpReferer"] ?? "https://property-intelligence.hashinaga.dev";
var orTitle      = p["OpenRouter:AppTitle"] ?? "Property Intelligence";

// Cada cliente tem retry + circuit breaker calibrados para o SLA do provider.
// O SafeFetchAsync do EnrichmentModule ainda impõe o timeout global de 5s por provider.

// OpenRouter (LLM) — best-effort, sem retry (falha silenciosa é comportamento correto)
builder.Services.AddHttpClient("openrouter", client =>
{
    client.BaseAddress = new Uri(urlOpenRouter);
    client.DefaultRequestHeaders.Add("Authorization",
        $"Bearer {builder.Configuration["OPENROUTER_API_KEY"] ?? string.Empty}");
    client.DefaultRequestHeaders.Add("HTTP-Referer", orReferer);
    client.DefaultRequestHeaders.Add("X-Title", orTitle);
    client.Timeout = TimeSpan.FromSeconds(12);
});
// Sem resilience handler no LLM — timeout do HttpClient é suficiente

// ViaCEP — lookup determinístico, 2 retries com backoff de 500ms
builder.Services.AddHttpClient("viacep", c => c.BaseAddress = new Uri(urlViaCep))
    .AddResilienceHandler("viacep-resilience", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay            = TimeSpan.FromMilliseconds(500),
            BackoffType      = DelayBackoffType.Exponential,
            UseJitter        = true,
            ShouldHandle     = args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException ||
                (int?)args.Outcome.Result?.StatusCode >= 500),
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration       = TimeSpan.FromSeconds(30),
            FailureRatio           = 0.5,
            MinimumThroughput      = 5,
            BreakDuration          = TimeSpan.FromSeconds(15),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(3));
    });

// Nominatim (OSM) — rate-limited externamente; 1 retry, timeout conservador
builder.Services.AddHttpClient("nominatim", c =>
{
    c.BaseAddress = new Uri(urlNominatim);
    c.DefaultRequestHeaders.Add("User-Agent", urlNominatimUA);
})
    .AddResilienceHandler("nominatim-resilience", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 1,
            Delay            = TimeSpan.FromSeconds(1),
            BackoffType      = DelayBackoffType.Constant,
            ShouldHandle     = args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException ||
                (int?)args.Outcome.Result?.StatusCode >= 500),
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration  = TimeSpan.FromSeconds(30),
            FailureRatio      = 0.5,
            MinimumThroughput = 3,
            BreakDuration     = TimeSpan.FromSeconds(20),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(4));
    });

// Overpass (OSM) — pode ser lento em pico; 2 retries com backoff
builder.Services.AddHttpClient("overpass", c => c.BaseAddress = new Uri(urlOverpass))
    .AddResilienceHandler("overpass-resilience", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay            = TimeSpan.FromSeconds(1),
            BackoffType      = DelayBackoffType.Exponential,
            UseJitter        = true,
            ShouldHandle     = args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException ||
                (int?)args.Outcome.Result?.StatusCode is 429 or >= 500),
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration  = TimeSpan.FromSeconds(30),
            FailureRatio      = 0.5,
            MinimumThroughput = 5,
            BreakDuration     = TimeSpan.FromSeconds(20),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(4));
    });

// IPTU API — free tier com limite de quota; 1 retry, circuit breaker agressivo
builder.Services.AddHttpClient("iptuapi", c => c.BaseAddress = new Uri(urlIptuApi))
    .AddResilienceHandler("iptuapi-resilience", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 1,
            Delay            = TimeSpan.FromSeconds(1),
            BackoffType      = DelayBackoffType.Constant,
            ShouldHandle     = args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException ||
                (int?)args.Outcome.Result?.StatusCode is >= 500),
            // 404 não é retried — IPTU não encontrado é resultado válido
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration  = TimeSpan.FromSeconds(60),
            FailureRatio      = 0.6,
            MinimumThroughput = 3,
            BreakDuration     = TimeSpan.FromSeconds(30),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(3));
    });
builder.Services.AddScoped<IExplainabilityService, LlmExplainabilityService>();

// Raw log repository (EF-backed)
// builder.Services.AddScoped<IRawLogRepository, EfRawLogRepository>(); // enable when T037 persistence is wired

// ── OpenTelemetry — OTLP exporter (T039 will wire remaining instrumentations) ─
var otlpEndpoint = builder.Configuration["GRAFANA_OTLP_ENDPOINT"]
    ?? Environment.GetEnvironmentVariable("GRAFANA_OTLP_ENDPOINT");

if (!string.IsNullOrEmpty(otlpEndpoint))
{
    var otlpToken = builder.Configuration["GRAFANA_OTLP_TOKEN"]
        ?? Environment.GetEnvironmentVariable("GRAFANA_OTLP_TOKEN");

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("property-intelligence-api",
            serviceVersion: "1.0.0"))
        .WithTracing(t =>
        {
            t.AddAspNetCoreInstrumentation();
            t.AddHttpClientInstrumentation();
            t.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                if (!string.IsNullOrEmpty(otlpToken))
                    o.Headers = $"Authorization=Basic {otlpToken}";
            });
        })
        .WithMetrics(m =>
        {
            m.AddAspNetCoreInstrumentation();
            m.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                if (!string.IsNullOrEmpty(otlpToken))
                    o.Headers = $"Authorization=Basic {otlpToken}";
            });
        });
}

// ── OpenTelemetry logs forwarding (when OTLP endpoint configured) ─────────────
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder.Logging.AddOpenTelemetry(otlpLogs =>
    {
        otlpLogs.IncludeScopes          = true;
        otlpLogs.IncludeFormattedMessage = true;
    });
}

// ────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Correlation ID + structured request logging (T058) ───────────────────────
app.Use(async (context, next) =>
{
    // Accept caller-supplied correlation ID or generate one
    var correlationId = context.Request.Headers.TryGetValue("X-Request-Id", out var hdr)
                        && !string.IsNullOrWhiteSpace(hdr)
        ? hdr.ToString()
        : Guid.NewGuid().ToString();

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;

    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var sw     = System.Diagnostics.Stopwatch.StartNew();

    await next(context);

    sw.Stop();

    var consumer = context.Items.TryGetValue("ApiConsumer", out var c)
        ? (c as ApiConsumer)?.Id.ToString()
        : null;

    var clientIp = context.Items.TryGetValue("ClientIp", out var ip)
        ? ip?.ToString()
        : context.Connection.RemoteIpAddress?.ToString();

    logger.LogInformation(
        "Request completed. " +
        "CorrelationId={CorrelationId} Operation={Operation} " +
        "StatusCode={StatusCode} DurationMs={DurationMs} " +
        "ApiConsumerId={ApiConsumerId} RequestIp={RequestIp}",
        correlationId,
        $"{context.Request.Method} {context.Request.Path}",
        context.Response.StatusCode,
        sw.ElapsedMilliseconds,
        consumer,
        clientIp);
});

// ── Auth middleware (T012) ────────────────────────────────────────────────────
app.UseMiddleware<ApiKeyAuthMiddleware>();

// ── Endpoints ────────────────────────────────────────────────────────────────
app.MapOpenApi();            // GET /openapi/v1.json — OpenAPI 3.1 nativo
app.MapHealthEndpoint();
app.MapAnalyzeEndpoint();

// (T039 complete)

app.Run();
