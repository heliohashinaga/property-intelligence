using NRules.Fluent.Dsl;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

/// <summary>
/// Scores the environment dimension from flood risk severity.
/// </summary>
public sealed class EnvironmentRules : Rule
{
    public override void Define()
    {
        PropertyFact property = default!;

        When()
            .Match<PropertyFact>(
                () => property,
                fact => fact.Profile.FloodRisk != null);

        Then()
            .Do(ctx => ctx.Insert(CreateScoringFact(property)));
    }

    private static ScoringFact CreateScoringFact(PropertyFact property)
    {
        var floodRisk = property.Profile.FloodRisk!;
        var score = Score(floodRisk);

        return new ScoringFact
        {
            Dimension = "environment",
            Points = score.Points,
            Reason = score.Reason,
        };
    }

    private static EnvironmentScore Score(FloodRiskData floodRisk)
    {
        var riskLevel = floodRisk.RiskLevel;
        var normalizedRiskLevel = riskLevel?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalizedRiskLevel))
        {
            return new EnvironmentScore(200, "flood_risk:none");
        }

        return normalizedRiskLevel switch
        {
            "low" => new EnvironmentScore(160, "flood_risk:low"),
            "moderate" => new EnvironmentScore(100, "flood_risk:moderate"),
            "high" => new EnvironmentScore(40, "flood_risk:high"),
            "critical" => new EnvironmentScore(0, "flood_risk:critical"),
            _ => new EnvironmentScore(200, $"flood_risk:unknown:{normalizedRiskLevel}"),
        };
    }

    private sealed record EnvironmentScore(int Points, string Reason);
}
