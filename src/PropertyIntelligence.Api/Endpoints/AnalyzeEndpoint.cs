using Microsoft.EntityFrameworkCore;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Services;

namespace PropertyIntelligence.Api.Endpoints;

public static class AnalyzeEndpoint
{
    public static IEndpointRouteBuilder MapAnalyzeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/property/analyze", HandleAsync)
            .WithName("AnalyzeProperty");

        return app;
    }

    private static async Task<IResult> HandleAsync(
        AnalyzeRequest request,
        HttpContext httpContext,
        IProviderRegistry providerRegistry,
        PropertyEnrichmentModule enrichmentModule,
        IPropertyAnalysisEngine analysisEngine,
        IExplainabilityService explainabilityService,
        PropertyIntelligenceDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("AnalyzeEndpoint");
        var requestStart = DateTime.UtcNow;

        if (request is null || string.IsNullOrWhiteSpace(request.Address))
        {
            return Results.UnprocessableEntity(new
            {
                error = "address_invalid",
                message = "O campo 'address' é obrigatório.",
                status = 422,
            });
        }

        var normalizedAddress = await NormalizeAddressAsync(request.Address, httpContext.RequestServices, ct);
        if (normalizedAddress is null)
        {
            return Results.UnprocessableEntity(new
            {
                error = "address_unrecognized",
                message = "Endereço não reconhecido ou ambíguo.",
                status = 422,
            });
        }

        // ── Persist address early to get its actual DB ID (T037) ──
        // This ensures the engine receives the correct persisted ID for the PropertyAnalysis
        var persistedAddress = await EnsureAddressAsync(dbContext, normalizedAddress, ct);
        normalizedAddress = persistedAddress;

        var enabledProviders = providerRegistry.GetEnabled()
            .Select(descriptor => descriptor.ProviderId)
            .ToArray();

        var profile = await enrichmentModule.EnrichAsync(normalizedAddress, ct);

        if (enabledProviders.Length > 0 && profile.ProvidersUnavailable.Count >= enabledProviders.Length)
        {
            return Results.StatusCode(503);
        }

        var apiConsumer = httpContext.Items.TryGetValue("ApiConsumer", out var apiConsumerObj)
            ? apiConsumerObj as ApiConsumer
            : null;

        var clientIp = httpContext.Items.TryGetValue("ClientIp", out var clientIpObj)
            ? clientIpObj?.ToString()
            : httpContext.Connection.RemoteIpAddress?.ToString();

        var analysis = analysisEngine.Analyze(
            profile,
            normalizedAddress.Id,
            apiConsumer?.Id ?? Guid.Empty,
            clientIp);

        var insight = await explainabilityService.GenerateInsightAsync(analysis, profile, ct)
            ?? BuildFallbackInsight(profile, analysis);

        // ── Audit persistence (T037) ──
        // Single transaction: insert analysis (address already persisted above), backfill raw logs
        using (var transaction = await dbContext.Database.BeginTransactionAsync(ct))
        {
            try
            {
                // INSERT property_analyses (append-only)
                // Address already persisted; analysis already has correct AddressId and ApiConsumerId
                dbContext.PropertyAnalyses.Add(analysis);
                await dbContext.SaveChangesAsync(ct);

                // Backfill data_provider_raw_logs with analysis_id
                // WHERE address_id = :addressId AND fetched_at >= :requestStart AND analysis_id IS NULL
                await BackfillRawLogsAsync(dbContext, normalizedAddress.Id, analysis.Id, requestStart, ct);

                await transaction.CommitAsync(ct);

                logger.LogInformation(
                    "Analysis persisted. AnalysisId={AnalysisId} AddressId={AddressId} Composite={Composite}",
                    analysis.Id,
                    analysis.AddressId,
                    analysis.CompositeScore);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                logger.LogWarning(
                    ex,
                    "Failed to persist analysis. CorrelationId={CorrelationId} Continuing without audit.",
                    httpContext.Items.TryGetValue("CorrelationId", out var corrId) ? corrId?.ToString() : "unknown");
                // Do NOT throw — return the response anyway (audit loss is non-fatal)
            }
        }

        var providersUnavailable = profile.ProvidersUnavailable
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var providersUsed = enabledProviders
            .Except(providersUnavailable, StringComparer.Ordinal)
            .ToArray();

        var warnings = providersUnavailable
            .Select(provider => new
            {
                provider,
                dimension = ResolveDimension(provider),
                message = $"Dados do provider '{provider}' indisponíveis nesta análise.",
            })
            .ToArray();

        var unavailableDimensionCount = providersUnavailable
            .Select(ResolveDimension)
            .Where(dimension => !string.Equals(dimension, "address", StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(dimension, "unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var responseScoreMax = Math.Max(0, 1000 - (unavailableDimensionCount * 200));
        var responseComposite = Math.Min(responseScoreMax, analysis.CompositeScore);

        var dimensions = analysis.DimensionScores
            .ToDictionary(
                score => score.Dimension,
                score => new
                {
                    score = score.Score,
                    max = score.Max,
                    trend = score.Trend is null ? null : TrendToContract(score.Trend.Value),
                    status = score.Status == DimensionStatus.Available ? "available" : "unavailable",
                },
                StringComparer.OrdinalIgnoreCase);

        var addressInfo = profile.AddressInfo;
        var responseAddress = new
        {
            normalized = addressInfo?.NormalizedAddress ?? normalizedAddress.NormalizedAddress,
            street = addressInfo?.StreetName ?? normalizedAddress.StreetName,
            number = addressInfo?.StreetNumber ?? normalizedAddress.StreetNumber,
            neighborhood = addressInfo?.Neighborhood ?? normalizedAddress.Neighborhood,
            city = addressInfo?.City ?? normalizedAddress.City,
            state = addressInfo?.State ?? normalizedAddress.State,
            postal_code = addressInfo?.PostalCode ?? normalizedAddress.PostalCode,
            coordinates = new
            {
                latitude = addressInfo?.Lat ?? normalizedAddress.Lat ?? 0,
                longitude = addressInfo?.Lng ?? normalizedAddress.Lng ?? 0,
            },
        };

        logger.LogInformation(
            "Property analysis produced response. AddressId={AddressId} Composite={Composite} Max={Max}",
            analysis.AddressId,
            analysis.CompositeScore,
            analysis.CompositeMax);

        return Results.Ok(new
        {
            address = responseAddress,
            score = new
            {
                composite = responseComposite,
                max = responseScoreMax,
                grade = analysis.Grade,
                dimensions,
            },
            risk_flags = analysis.RiskFlags,
            opportunity_flags = analysis.OpportunityFlags,
            insight,
            insight_unavailable = false,
            warnings,
            providers_used = providersUsed,
            providers_unavailable = providersUnavailable,
            cached = profile.AllFromCache,
            analysis_id = analysis.Id,
            analyzed_at = analysis.CreatedAt,
        });
    }

    private static async Task<PropertyAddress?> NormalizeAddressAsync(
        string rawAddress,
        IServiceProvider serviceProvider,
        CancellationToken ct)
    {
        var normalizer = serviceProvider.GetService<IAddressNormalizer>();
        if (normalizer is not null)
        {
            return await normalizer.NormalizeAsync(rawAddress, ct);
        }

        if (string.IsNullOrWhiteSpace(rawAddress) || rawAddress.Trim().Length < 5)
        {
            return null;
        }

        var parts = rawAddress.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return new PropertyAddress
        {
            NormalizedAddress = rawAddress.Trim(),
            StreetName = parts.Length > 0 ? parts[0] : rawAddress.Trim(),
            StreetNumber = parts.Length > 1 ? parts[1] : null,
            Neighborhood = null,
            City = parts.Length > 2 ? parts[2] : "São Paulo",
            State = "SP",
        };
    }

    private static string ResolveDimension(string providerId)
        => providerId switch
        {
            "mock_address" => "address",
            "mock_security" => "security",
            "mock_mobility" => "mobility",
            "mock_environment" => "environment",
            "mock_health" or "mock_school" => "infrastructure",
            "mock_appreciation" => "appreciation",
            "mock_urban_context" => "urban_context",
            _ => "unknown",
        };

    private static string? TrendToContract(TrendDirection trend)
        => trend switch
        {
            TrendDirection.Improving => "improving",
            TrendDirection.Stable => "stable",
            TrendDirection.Worsening => "worsening",
            _ => null,
        };

    private static string BuildFallbackInsight(PropertyProfile profile, PropertyAnalysis analysis)
        => $"Análise gerada para {profile.Address.NormalizedAddress}. " +
           $"Score {analysis.CompositeScore}/{analysis.CompositeMax} ({analysis.Grade}). " +
           $"Foram identificados {analysis.RiskFlags.Count} riscos e {analysis.OpportunityFlags.Count} oportunidades.";

    private static async Task<PropertyAddress> EnsureAddressAsync(
        PropertyIntelligenceDbContext dbContext,
        PropertyAddress normalizedAddress,
        CancellationToken ct)
    {
        // ON CONFLICT normalized_address DO NOTHING, then fetch the actual ID
        var existing = await dbContext.PropertyAddresses
            .FirstOrDefaultAsync(a => a.NormalizedAddress == normalizedAddress.NormalizedAddress, ct);

        if (existing is not null)
        {
            return existing;
        }

        // Create new with only the required fields; let EF Core + DB defaults handle CreatedAt
        var newAddress = new PropertyAddress
        {
            NormalizedAddress = normalizedAddress.NormalizedAddress,
            StreetName = normalizedAddress.StreetName,
            StreetNumber = normalizedAddress.StreetNumber,
            Neighborhood = normalizedAddress.Neighborhood,
            City = normalizedAddress.City,
            State = normalizedAddress.State,
            PostalCode = normalizedAddress.PostalCode,
            Lat = normalizedAddress.Lat,
            Lng = normalizedAddress.Lng,
        };
        dbContext.PropertyAddresses.Add(newAddress);
        await dbContext.SaveChangesAsync(ct);
        return newAddress;
    }

    private static async Task BackfillRawLogsAsync(
        PropertyIntelligenceDbContext dbContext,
        Guid addressId,
        Guid analysisId,
        DateTime requestStart,
        CancellationToken ct)
    {
        // Raw logs created during enrichment but not yet linked to analysis_id
        // Use raw SQL UPDATE since AnalysisId is init-only
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE data_provider_raw_logs SET analysis_id = {analysisId} WHERE address_id = {addressId} AND fetched_at >= {new DateTimeOffset(requestStart, TimeSpan.Zero)} AND analysis_id IS NULL",
            ct);
    }

    private sealed record AnalyzeRequest(string Address);
}
