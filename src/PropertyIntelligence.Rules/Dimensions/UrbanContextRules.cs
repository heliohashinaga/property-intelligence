using NRules.Fluent.Dsl;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

// ── Urban Context dimension rules ─────────────────────────────────────────────
// Additive (capped at 200 by the engine).
// CensusData.MedianIncomeGroup: 1–5 scale (5 = highest) from IBGE Censo 2022.
// Optimal PopulationDensity range: 5000–20000 residents/km².

public sealed class UrbanContextNoCensusRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CensusData == null);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "urban_context",
                Points    = 60,
                Reason    = "No census data — neutral urban score",
                Trend     = "stable"
            }));
    }
}

public sealed class UrbanContextHighIncomeRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CensusData != null &&
                     f.Profile.CensusData.MedianIncomeGroup >= 4);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "urban_context",
                Points    = 120,
                Reason    = "High income census sector (group 4–5)",
                Trend     = "stable"
            }));
    }
}

public sealed class UrbanContextMidIncomeRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CensusData != null &&
                     f.Profile.CensusData.MedianIncomeGroup == 3);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "urban_context",
                Points    = 80,
                Reason    = "Middle income census sector (group 3)",
                Trend     = "stable"
            }));
    }
}

public sealed class UrbanContextLowerMidIncomeRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CensusData != null &&
                     f.Profile.CensusData.MedianIncomeGroup == 2);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "urban_context",
                Points    = 40,
                Reason    = "Lower-middle income census sector (group 2)",
                Trend     = "stable"
            }));
    }
}

public sealed class UrbanContextLowIncomeRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CensusData != null &&
                     f.Profile.CensusData.MedianIncomeGroup <= 1);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "urban_context",
                Points    = 20,
                Reason    = "Low income census sector (group 1)",
                Trend     = "stable"
            }));
    }
}

public sealed class UrbanContextOptimalDensityRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.CensusData != null &&
                     f.Profile.CensusData.PopulationDensity >= 5000 &&
                     f.Profile.CensusData.PopulationDensity <= 20000);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "urban_context",
                Points    = 60,
                Reason    = "Optimal urban density (5k–20k residents/km²)",
                Trend     = "stable"
            }));
    }
}
