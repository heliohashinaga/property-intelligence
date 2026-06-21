using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.Extensions.Options;
using PropertyIntelligence.Api.Endpoints;
using PropertyIntelligence.Api.Middleware;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Services;
using PropertyIntelligence.Providers.Registry;
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
var databaseUrl = builder.Configuration["DATABASE_URL"]
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=property_intelligence;Username=property_intelligence;Password=property_intelligence";

builder.Services.AddDbContext<PropertyIntelligenceDbContext>(opts =>
    opts.UseNpgsql(databaseUrl,
        npgsql => npgsql.UseNetTopologySuite()),
    ServiceLifetime.Scoped);

// ── Redis ─────────────────────────────────────────────────────────────────────
var redisUrl = (builder.Configuration["REDIS_URL"]
    ?? Environment.GetEnvironmentVariable("REDIS_URL")
    ?? "redis://localhost:6379")
    .Replace("redis://", "");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisUrl));

// ── Provider registry (T086) ───────────────────────────────────────────────
// The catalog is configuration-driven (appsettings `Providers:Catalog` array),
// so enabling/disabling a data source requires no endpoint or engine rewrite.
// T038 provides the `appsettings.Mock.json` profile with the 7 mock providers.
builder.Services.Configure<ProviderCatalogOptions>(builder.Configuration.GetSection("Providers"));

builder.Services.AddSingleton<IProviderRegistry>(sp =>
{
    var catalog = sp.GetRequiredService<IOptions<ProviderCatalogOptions>>().Value;
    var descriptors = catalog.Catalog.ToDescriptors();
    return new ProviderRegistry(descriptors);
});

builder.Services.AddScoped<PropertyEnrichmentModule>();

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
app.MapHealthEndpoint();

// TODO T039: register all providers, engine, enrichment module, explainability service

app.Run();
