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
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("AnalyzeEndpoint");

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

    private sealed record AnalyzeRequest(string Address);
}
