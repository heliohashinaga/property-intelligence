using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Api.Logging;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Services;

namespace PropertyIntelligence.Api.Endpoints;

/// <summary>
/// POST /v1/property/analyze — the main scoring endpoint.
/// Orchestrates: normalize → enrich → analyze → explain → persist → respond.
/// </summary>
public static partial class AnalyzeEndpoint
{
    public static IEndpointRouteBuilder MapAnalyzeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/property/analyze", HandleAsync)
           .RequireAuthorization()
           .WithName("AnalyzeProperty")
           .WithSummary("Analyze a Brazilian property address")
           .WithDescription("Scores a property across 6 dimensions (security, mobility, infrastructure, environment, appreciation, urban_context) using public data sources and generates a PT-BR AI insight.")
           .Produces(200)
           .Produces(400)
           .Produces(401)
           .Produces(422)
           .Produces(503);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        AnalyzeRequest          request,
        HttpContext             ctx,
        IAddressNormalizer      normalizer,
        PropertyEnrichmentModule enricher,
        IPropertyAnalysisEngine engine,
        IExplainabilityService  llm,
        PropertyIntelligenceDbContext db,
        ICacheService           cacheService,
        TimeProvider            timeProvider,
        ILogger<AnalyzeEndpointMarker> logger,
        CancellationToken       ct)
    {
        var sw            = Stopwatch.StartNew();
        var correlationId = ctx.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
        var apiConsumer   = ctx.Items["ApiConsumer"] as ApiConsumer;
        var clientIp      = ctx.Items["ClientIp"]?.ToString();
        var requestStart  = timeProvider.GetUtcNow();

        // ── Cache-Control: no-cache bypass (FR-007, T088) ─────────────────────
        // When present, skip the composed analysis cache and force a fresh analysis.
        // Provider-level Redis TTLs are NOT invalidated.
        var forceRefresh = ctx.Request.Headers["Cache-Control"]
            .ToString()
            .Contains("no-cache", StringComparison.OrdinalIgnoreCase);

        // ── 1. Normalize address ──────────────────────────────────────────────
        PropertyAddress? address;
        try
        {
            address = await normalizer.NormalizeAsync(request.Address, ct);
        }
        catch (Exception ex)
        {
            LogNormalizationFailed(logger, ex, correlationId, AddressLogEnricher.Hash(request.Address));
            address = null;
        }

        if (address is null)
        {
            return Results.Json(
                new { error = "address_unrecognized", message = "Address not recognized or too ambiguous to analyze." },
                statusCode: 422);
        }

        // Invalidate analysis cache when Cache-Control: no-cache requested
        if (forceRefresh)
            await cacheService.InvalidateAnalysisAsync(address.NormalizedAddress, ct);

        // ── 3. Upsert property_addresses ──────────────────────────────────────
        var existingAddr = await db.PropertyAddresses
            .FirstOrDefaultAsync(a => a.NormalizedAddress == address.NormalizedAddress, ct);

        if (existingAddr is not null)
            address = existingAddr;
        else
        {
            db.PropertyAddresses.Add(address);
            await db.SaveChangesAsync(ct);
        }

        // ── 4. Enrich (parallel providers) ───────────────────────────────────
        var profile = await enricher.EnrichAsync(address, correlationId, ct);

        // ── 5. Analyze (NRules engine) ────────────────────────────────────────
        var consumerId = apiConsumer?.Id ?? Guid.Empty;
        var analysis   = engine.Analyze(profile, address.Id, consumerId, clientIp);

        // 503 if fewer than 3 dimensions have data
        var availableCount = analysis.DimensionScores.Count(d => d.Status == DimensionStatus.Available);
        if (availableCount < 3)
        {
            return Results.Json(
                new
                {
                    error   = "insufficient_data",
                    message = "Insufficient data to generate a reliable analysis. Please try again later.",
                    providers_unavailable = profile.ProvidersUnavailable,
                },
                statusCode: 503);
        }

        // ── 6. LLM insight (best-effort) ──────────────────────────────────────
        var insight = await llm.GenerateInsightAsync(analysis, profile, ct);

        // Patch analysis with LLM result and resolved metadata
        analysis = analysis with
        {
            Insight            = insight,
            InsightUnavailable = insight is null,
            LlmModel           = ctx.RequestServices
                                    .GetService<IConfiguration>()
                                    ?["LLM_MODEL"] ?? "unknown",
            Cached             = !forceRefresh && analysis.Cached,
        };

        // ── 7. Persist property_analyses (append-only) ────────────────────────
        await PersistAnalysisAsync(db, analysis, address.Id, requestStart, ct);

        sw.Stop();
        LogAnalysisComplete(logger,
            correlationId, AddressLogEnricher.Hash(address.NormalizedAddress),
            analysis.CompositeScore, analysis.CompositeMax, analysis.Grade, sw.ElapsedMilliseconds);

        // ── OpenTelemetry metrics (T071) ──────────────────────────────────────────
        AppTelemetry.AnalysisDuration.Record(sw.ElapsedMilliseconds);
        AppTelemetry.ScoreComposite.Record(
            analysis.CompositeScore,
            new KeyValuePair<string, object?>("grade", analysis.Grade));

        // ── 8. Build response ─────────────────────────────────────────────────
        return Results.Ok(BuildResponse(address, analysis));
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private static async Task PersistAnalysisAsync(
        PropertyIntelligenceDbContext db,
        PropertyAnalysis analysis,
        Guid addressId,
        DateTimeOffset requestStart,
        CancellationToken ct)
    {
        db.PropertyAnalyses.Add(analysis);
        await db.SaveChangesAsync(ct);

        // Backfill analysis_id on raw logs created during this request
        // EF Core 10: type-safe ExecuteUpdateAsync replaces raw SQL
        await db.DataProviderRawLogs
            .Where(l => l.AddressId == addressId
                     && l.AnalysisId == null
                     && l.FetchedAt >= requestStart)
            .ExecuteUpdateAsync(
                s => s.SetProperty(l => l.AnalysisId, analysis.Id), ct);
    }

    // ── Response mapping ──────────────────────────────────────────────────────

    private static object BuildResponse(PropertyAddress address, PropertyAnalysis analysis)
    {
        return new
        {
            address = new
            {
                normalized   = address.NormalizedAddress,
                street       = address.StreetName,
                number       = address.StreetNumber,
                neighborhood = address.Neighborhood,
                city         = address.City,
                state        = address.State,
                postal_code  = address.PostalCode,
                coordinates  = address.Lat.HasValue ? new { latitude = address.Lat, longitude = address.Lng } : null,
            },
            score = new
            {
                composite = analysis.CompositeScore,
                max       = analysis.CompositeMax,
                grade     = analysis.Grade,
                dimensions = analysis.DimensionScores.ToDictionary(
                    d => d.Dimension,
                    d => (object)new
                    {
                        score  = d.Score,
                        max    = d.Max,
                        trend  = d.Trend?.ToString().ToLowerInvariant(),
                        status = d.Status.ToString().ToLowerInvariant(),
                    }),
            },
            risk_flags        = analysis.RiskFlags,
            opportunity_flags = analysis.OpportunityFlags,
            insight           = analysis.Insight,
            insight_unavailable = analysis.InsightUnavailable,
            warnings = analysis.Warnings.Select(w => new
            {
                code      = w.Code,
                dimension = w.Dimension,
                message   = w.Message,
            }),
            providers_used        = analysis.ProvidersUsed,
            providers_unavailable = analysis.ProvidersUnavailable,
            cached                = analysis.Cached,
            analysis_id           = analysis.Id,
            analyzed_at           = analysis.CreatedAt,
        };
    }
}

// [LoggerMessage] partial definitions — source-generated at compile time, zero allocations
public static partial class AnalyzeEndpoint
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Address normalization failed. CorrelationId={CorrelationId} Input={Input}")]
    private static partial void LogNormalizationFailed(
        ILogger logger, Exception ex, string correlationId, string? input);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Analysis complete. CorrelationId={CorrelationId} Address={Address} Composite={Composite}/{Max} Grade={Grade} DurationMs={DurationMs}")]
    private static partial void LogAnalysisComplete(
        ILogger logger, string correlationId, string address,
        int composite, int max, string grade, long durationMs);
}

/// <summary>Marker type for ILogger injection (avoids generic open-type issues).</summary>
public sealed class AnalyzeEndpointMarker { }

/// <summary>Request DTO for POST /v1/property/analyze.</summary>
public sealed record AnalyzeRequest(
    [Required][StringLength(500, MinimumLength = 3)]
    string Address);
