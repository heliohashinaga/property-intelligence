using NRules.Fluent.Dsl;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

// ── Appreciation dimension rules ──────────────────────────────────────────────
// Additive (capped at 200 by the engine).
// MVP: IptuData has ValorVenal and ZoningClass (no historical time series).
// Full CAGR trend analysis deferred to Phase 4 (US2).

public sealed class AppreciationNoIptuDataRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.IptuData == null);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "appreciation",
                Points    = 80,
                Reason    = "No IPTU data — neutral appreciation score",
                Trend     = "stable"
            }));
    }
}

public sealed class AppreciationHighValueZoneRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.IptuData != null &&
                     f.Profile.IptuData.ValorVenal >= 1_000_000m);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "appreciation",
                Points    = 120,
                Reason    = "High assessed value (≥ R$1M) — premium location",
                Trend     = "improving"
            }));
    }
}

public sealed class AppreciationMidValueZoneRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.IptuData != null &&
                     f.Profile.IptuData.ValorVenal >= 300_000m &&
                     f.Profile.IptuData.ValorVenal < 1_000_000m);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "appreciation",
                Points    = 80,
                Reason    = "Mid-range assessed value (R$300k–1M)",
                Trend     = "stable"
            }));
    }
}

public sealed class AppreciationLowValueZoneRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.IptuData != null &&
                     f.Profile.IptuData.ValorVenal > 0m &&
                     f.Profile.IptuData.ValorVenal < 300_000m);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "appreciation",
                Points    = 40,
                Reason    = "Below-average assessed value (<R$300k)",
                Trend     = "stable"
            }));
    }
}

public sealed class AppreciationPremiumZoningRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.IptuData != null &&
                     f.Profile.IptuData.ZoningClass != null &&
                     (f.Profile.IptuData.ZoningClass.StartsWith("ZC")  ||
                      f.Profile.IptuData.ZoningClass.StartsWith("ZM-3") ||
                      f.Profile.IptuData.ZoningClass.StartsWith("ZEU") ||
                      f.Profile.IptuData.ZoningClass.StartsWith("ZOE")));
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "appreciation",
                Points    = 40,
                Reason    = "Premium zoning class — high density / commercial corridor",
                Trend     = "improving"
            }));
    }
}
