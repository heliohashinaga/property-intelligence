using NRules.Fluent.Dsl;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

// ── Infrastructure dimension rules ────────────────────────────────────────────
// Additive (capped at 200 by the engine).
// Draws from HealthData, SchoolData, and PoiData.Pharmacies1km.

public sealed class InfrastructureNoDataRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.HealthData == null && f.Profile.SchoolData == null);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "infrastructure",
                Points    = 60,
                Reason    = "No health or school data — neutral score",
                Trend     = "stable"
            }));
    }
}

public sealed class InfrastructureHospitalWithin2kmRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.HealthData != null &&
                     f.Profile.HealthData.HospitalsWithin2km >= 1);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "infrastructure",
                Points    = 80,
                Reason    = "Hospital within 2km",
                Trend     = "stable"
            }));
    }
}

public sealed class InfrastructureMultipleHospitalsRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.HealthData != null &&
                     f.Profile.HealthData.HospitalsWithin2km >= 3);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "infrastructure",
                Points    = 20,
                Reason    = "Multiple hospitals within 2km",
                Trend     = "stable"
            }));
    }
}

public sealed class InfrastructureClinicRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.HealthData != null &&
                     f.Profile.HealthData.ClinicsWith2km >= 1);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "infrastructure",
                Points    = 30,
                Reason    = "Primary care clinic (UBS/UPA) within 2km",
                Trend     = "stable"
            }));
    }
}

public sealed class InfrastructureQualitySchoolRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.SchoolData != null &&
                     f.Profile.SchoolData.NearestSchoolIdeb >= 7.0);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "infrastructure",
                Points    = 50,
                Reason    = "Quality public school nearby (IDEB ≥ 7.0)",
                Trend     = "stable"
            }));
    }
}

public sealed class InfrastructureSchoolPresenceRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.SchoolData != null &&
                     f.Profile.SchoolData.SchoolsWithin2km >= 1 &&
                     (f.Profile.SchoolData.NearestSchoolIdeb == null ||
                      f.Profile.SchoolData.NearestSchoolIdeb < 7.0));
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "infrastructure",
                Points    = 20,
                Reason    = "Public school within 2km",
                Trend     = "stable"
            }));
    }
}

public sealed class InfrastructurePharmacyRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null!;
        When()
            .Match<PropertyFact>(() => fact,
                f => f.Profile.PoiData != null &&
                     f.Profile.PoiData.Pharmacies1km >= 1);
        Then()
            .Do(ctx => ctx.Insert(new ScoringFact
            {
                Dimension = "infrastructure",
                Points    = 20,
                Reason    = "Pharmacy within 1km",
                Trend     = "stable"
            }));
    }
}
