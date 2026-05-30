using NRules.Fluent.Dsl;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

// ── Mobility dimension rules ──────────────────────────────────────────────────
// Additive (capped at 200 by the engine). Based on PoiData transit stop counts.
// At MVP PoiData provides TransitStops500m and TransitStops1km (no metro/bus split).

public sealed class MobilityNoPoiDataRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.PoiData == null);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "mobility",
                Points    = 60,
                Reason    = "No POI data — neutral mobility score",
                Trend     = "stable"
            }));
    }
}

public sealed class MobilityExcellentTransitRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.PoiData != null &&
                     f.Profile.PoiData.TransitStops500m >= 3);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "mobility",
                Points    = 120,
                Reason    = "Excellent transit density within 500m (3+ stops)",
                Trend     = "improving"
            }));
    }
}

public sealed class MobilityGoodTransitRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.PoiData != null &&
                     f.Profile.PoiData.TransitStops500m >= 1 &&
                     f.Profile.PoiData.TransitStops500m < 3);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "mobility",
                Points    = 80,
                Reason    = "Good transit access within 500m",
                Trend     = "stable"
            }));
    }
}

public sealed class MobilityTransitIn1kmRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.PoiData != null &&
                     f.Profile.PoiData.TransitStops500m == 0 &&
                     f.Profile.PoiData.TransitStops1km >= 3);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "mobility",
                Points    = 50,
                Reason    = "Multiple transit stops within 1km",
                Trend     = "stable"
            }));
    }
}

public sealed class MobilityLimitedTransitRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.PoiData != null &&
                     f.Profile.PoiData.TransitStops500m == 0 &&
                     f.Profile.PoiData.TransitStops1km >= 1 &&
                     f.Profile.PoiData.TransitStops1km < 3);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "mobility",
                Points    = 30,
                Reason    = "Limited transit — 1 stop within 1km",
                Trend     = "stable"
            }));
    }
}

public sealed class MobilityNoTransitRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.PoiData != null &&
                     f.Profile.PoiData.TransitStops500m == 0 &&
                     f.Profile.PoiData.TransitStops1km == 0);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "mobility",
                Points    = 10,
                Reason    = "No transit stops within 1km",
                Trend     = "worsening"
            }));
    }
}

public sealed class MobilityPoiDensityBonusRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.PoiData != null &&
                     f.Profile.PoiData.Pois2km >= 50);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "mobility",
                Points    = 40,
                Reason    = "High urban POI density — walkable neighbourhood",
                Trend     = "stable"
            }));
    }
}
