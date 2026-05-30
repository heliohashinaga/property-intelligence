using NRules.Fluent.Dsl;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

// ── Security dimension rules ──────────────────────────────────────────────────
// Each class is an independent NRules rule. Points are NOT additive:
// exactly ONE rule fires per analysis session for the security dimension.
// Scores are based on CrimeData.CrimeRatePer100k (crimes per 100k residents/year).

public sealed class SecurityNoCrimeDataRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CrimeData == null);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "security",
                Points    = 100,
                Reason    = "No crime data — neutral score",
                Trend     = "stable"
            }));
    }
}

public sealed class SecurityVeryLowCrimeRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CrimeData != null &&
                     f.Profile.CrimeData.CrimeRatePer100k != null &&
                     f.Profile.CrimeData.CrimeRatePer100k < 50);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "security",
                Points    = 180,
                Reason    = "Very low crime rate (<50/100k)",
                Trend     = "stable"
            }));
    }
}

public sealed class SecurityLowCrimeRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CrimeData != null &&
                     f.Profile.CrimeData.CrimeRatePer100k >= 50 &&
                     f.Profile.CrimeData.CrimeRatePer100k < 100);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "security",
                Points    = 140,
                Reason    = "Low crime rate (50–100/100k)",
                Trend     = "stable"
            }));
    }
}

public sealed class SecurityModerateCrimeRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CrimeData != null &&
                     f.Profile.CrimeData.CrimeRatePer100k >= 100 &&
                     f.Profile.CrimeData.CrimeRatePer100k < 200);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "security",
                Points    = 100,
                Reason    = "Moderate crime rate (100–200/100k)",
                Trend     = "stable"
            }));
    }
}

public sealed class SecurityHighCrimeRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CrimeData != null &&
                     f.Profile.CrimeData.CrimeRatePer100k >= 200 &&
                     f.Profile.CrimeData.CrimeRatePer100k < 300);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "security",
                Points    = 60,
                Reason    = "High crime rate (200–300/100k)",
                Trend     = "worsening"
            }));
    }
}

public sealed class SecurityVeryHighCrimeRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CrimeData != null &&
                     f.Profile.CrimeData.CrimeRatePer100k >= 300);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "security",
                Points    = 20,
                Reason    = "Very high crime rate (>300/100k)",
                Trend     = "worsening"
            }));
    }
}
