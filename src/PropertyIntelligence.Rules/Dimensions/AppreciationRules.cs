using NRules.Fluent.Dsl;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

/// <summary>
/// Scores the appreciation dimension from public-data-friendly cadastral proxies.
/// </summary>
public sealed class AppreciationRules : Rule
{
    public override void Define()
    {
        PropertyFact property = default!;

        When()
            .Match<PropertyFact>(
                () => property,
                fact => fact.Profile.IptuData != null);

        Then()
            .Do(ctx => ctx.Insert(CreateScoringFact(property)));
    }

    private static ScoringFact CreateScoringFact(PropertyFact property)
    {
        var iptuData = property.Profile.IptuData!;
        var score = Score(iptuData);

        return new ScoringFact
        {
            Dimension = "appreciation",
            Points = score.Points,
            Reason = score.Reason,
        };
    }

    private static AppreciationScore Score(IptuData iptuData)
    {
        var zoningPoints = ScoreZoning(iptuData.ZoningClass);
        var cadastralPoints = ScoreValorVenal(iptuData.ValorVenal);
        var trendPoints = ScoreTrend(iptuData.AppreciationTrend);
        var totalPoints = Math.Min(200, zoningPoints + cadastralPoints + trendPoints);

        var reason = $"appreciation:zoning={NormalizeZoning(iptuData.ZoningClass) ?? "unknown"};valorVenal={(iptuData.ValorVenal?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "null")};trend={(iptuData.AppreciationTrend?.ToString() ?? "null")}";

        return new AppreciationScore(totalPoints, reason);
    }

    private static int ScoreZoning(string? zoningClass)
    {
        var normalized = NormalizeZoning(zoningClass);
        if (string.IsNullOrWhiteSpace(normalized)) return 0;

        return normalized switch
        {
            "ZEU" => 100,
            "ZEUP" => 95,
            "ZC" => 75,
            "ZM-1" => 60,
            _ when normalized.StartsWith("ZM", StringComparison.Ordinal) => 55,
            _ => 30,
        };
    }

    private static int ScoreValorVenal(decimal? valorVenal)
    {
        if (!valorVenal.HasValue) return 0;
        if (valorVenal.Value >= 1_000_000m) return 20;
        if (valorVenal.Value >= 500_000m) return 10;
        return 5;
    }

    private static int ScoreTrend(TrendDirection? trend)
    {
        return trend switch
        {
            TrendDirection.Improving => 40,
            TrendDirection.Stable => 20,
            TrendDirection.Worsening => 0,
            _ => 0,
        };
    }

    private static string? NormalizeZoning(string? zoningClass)
        => zoningClass?.Trim().ToUpperInvariant();

    private sealed record AppreciationScore(int Points, string Reason);
}
