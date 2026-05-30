using NRules.Fluent.Dsl;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

// ── Environment dimension rules ───────────────────────────────────────────────
// NOT additive — exactly one rule fires per analysis session.
// Based on FloodRiskData.RiskLevel: null | "low" | "moderate" | "high" | "critical".

public sealed class EnvironmentNoFloodDataRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.FloodRisk == null);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "environment",
                Points    = 200,
                Reason    = "No flood zone data — maximum score",
                Trend     = "stable"
            }));
    }
}

public sealed class EnvironmentNoFloodZoneRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.FloodRisk != null &&
                     f.Profile.FloodRisk.RiskLevel == null);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "environment",
                Points    = 200,
                Reason    = "Property outside all flood zones",
                Trend     = "stable"
            }));
    }
}

public sealed class EnvironmentLowFloodRiskRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.FloodRisk != null &&
                     f.Profile.FloodRisk.RiskLevel == "low");
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "environment",
                Points    = 160,
                Reason    = "Low flood risk zone",
                Trend     = "stable"
            }));
    }
}

public sealed class EnvironmentModerateFloodRiskRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.FloodRisk != null &&
                     f.Profile.FloodRisk.RiskLevel == "moderate");
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "environment",
                Points    = 100,
                Reason    = "Moderate flood risk zone",
                Trend     = "worsening"
            }));
    }
}

public sealed class EnvironmentHighFloodRiskRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.FloodRisk != null &&
                     f.Profile.FloodRisk.RiskLevel == "high");
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "environment",
                Points    = 40,
                Reason    = "High flood risk zone",
                Trend     = "worsening"
            }));
    }
}

public sealed class EnvironmentCriticalFloodRiskRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.FloodRisk != null &&
                     f.Profile.FloodRisk.RiskLevel == "critical");
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "environment",
                Points    = 0,
                Reason    = "Critical flood risk zone",
                Trend     = "worsening"
            }));
    }
}
