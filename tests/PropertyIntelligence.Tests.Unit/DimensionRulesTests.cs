using NSubstitute;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using Shouldly;

namespace PropertyIntelligence.Tests.Unit;

/// <summary>
/// TDD red-phase tests for all 6 NRules dimension scoring rules.
/// Tests are written against the IPropertyAnalysisEngine interface contract.
///
/// RED PHASE: Tests are skipped until T027–T034 (NRules Facts + Dimensions + Engine) are complete.
/// To activate: remove the Skip attribute and replace the NSubstitute mock with the real
/// PropertyAnalysisEngine implementation.
///
/// Scoring thresholds (from tasks.md T028–T033):
///   Security:      Per100k &lt; 50  → score ≥ 160  |  Per100k &gt; 300 → score ≤ 40
///   Mobility:      metro within 500m → score ≥ 150  |  no transit → score ≤ 40
///   Infrastructure: hospital within 2km → score ≥ 100
///   Environment:   null risk → 200  |  critical → 0
///   Appreciation:  positive CAGR → score ≥ 120
///   UrbanContext:  income group ≥ 8 → score ≥ 130
/// </summary>
[Trait("Category", "Unit")]
public sealed class DimensionRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PropertyAddress MakeAddress() => new()
    {
        NormalizedAddress = "Rua Augusta, 1500 - Consolação, São Paulo - SP",
        City = "São Paulo", State = "SP",
        Lat = -23.5563, Lng = -46.6543
    };

    private static PropertyAnalysis MakeAnalysis(
        int security = 100, int mobility = 100, int infrastructure = 100,
        int environment = 100, int appreciation = 100, int urbanContext = 100,
        bool securityUnavailable = false)
    {
        var dims = new List<DimensionScore>
        {
            securityUnavailable
                ? DimensionScore.Unavailable("security")
                : DimensionScore.Available("security",      security,      TrendDirection.Stable),
            DimensionScore.Available("mobility",      mobility,      TrendDirection.Stable),
            DimensionScore.Available("infrastructure", infrastructure, TrendDirection.Stable),
            DimensionScore.Available("environment",   environment,   TrendDirection.Stable),
            DimensionScore.Available("appreciation",  appreciation,  TrendDirection.Stable),
            DimensionScore.Available("urban_context", urbanContext,  TrendDirection.Stable)
        };

        var available = dims.Where(d => d.Status == DimensionStatus.Available).ToList();
        var composite = available.Sum(d => d.Score ?? 0);
        var max       = available.Count * 200;

        return new PropertyAnalysis
        {
            AddressId      = Guid.NewGuid(),
            ApiConsumerId  = Guid.NewGuid(),
            CompositeScore = composite,
            CompositeMax   = max,
            Grade          = "B",
            DimensionScores      = dims,
            Warnings       = [],
            RiskFlags      = [],
            OpportunityFlags = [],
            Insight        = null,
            InsightUnavailable = false,
            ProvidersUsed  = ["viacep", "overpass", "ssp_sp", "ana_snirh", "ibge_census", "cnes", "inep", "iptu_api"],
            ProvidersUnavailable = [],
            LlmModel       = "test",
            RulesVersion   = "1.0.0",
            RequestIp      = null,
            // CreatedAt defaults to DateTimeOffset.UtcNow
        };
    }

    // ── Security dimension ───────────────────────────────────────────────────

    [Fact(Skip = "Red phase — SecurityRules not implemented yet (T028)")]
    public void Security_LowCrime_ScoresHigh()
    {
        // Profile: CrimeRatePer100k = 30 (<50) → security score MUST be >= 160
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            CrimeData = new CrimeData { CrimeRatePer100k = 30.0 },
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.CrimeData!.CrimeRatePer100k < 50),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(security: 180));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "security");
        dim.Status.ShouldBe(DimensionStatus.Available);
        dim.Score.ShouldNotBeNull();
        dim.Score.ShouldNotBeNull();
        dim.Score!.Value.ShouldBeGreaterThanOrEqualTo(160);
    }

    [Fact(Skip = "Red phase — SecurityRules not implemented yet (T028)")]
    public void Security_HighCrime_ScoresLow()
    {
        // Profile: CrimeRatePer100k = 350 (>300) → security score MUST be <= 40
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            CrimeData = new CrimeData { CrimeRatePer100k = 350.0 },
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.CrimeData!.CrimeRatePer100k > 300),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(security: 20));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "security");
        dim.Score.ShouldNotBeNull();
        dim.Score.ShouldNotBeNull();
        dim.Score!.Value.ShouldBeLessThanOrEqualTo(40);
    }

    [Fact(Skip = "Red phase — SecurityRules not implemented yet (T028)")]
    public void Security_ProviderUnavailable_DimensionIsUnavailable()
    {
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            CrimeData = null,
            ProvidersUnavailable = ["ssp_sp"]
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.CrimeData == null),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(securityUnavailable: true));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "security");
        dim.Status.ShouldBe(DimensionStatus.Unavailable);
        dim.Score.ShouldBeNull();
    }

    // ── Mobility dimension ───────────────────────────────────────────────────

    [Fact(Skip = "Red phase — MobilityRules not implemented yet (T029)")]
    public void Mobility_MetroNearby_ScoresHigh()
    {
        // TransitStops500m >= 1 (metro station) → mobility score MUST be >= 150
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            PoiData = new PoiData { TransitStops500m = 2, TransitStops1km = 5, Pois2km = 30 },
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.PoiData!.TransitStops500m >= 1),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(mobility: 185));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "mobility");
        dim.Score.ShouldNotBeNull();
        dim.Score!.Value.ShouldBeGreaterThanOrEqualTo(150);
    }

    [Fact(Skip = "Red phase — MobilityRules not implemented yet (T029)")]
    public void Mobility_NoTransit_ScoresLow()
    {
        // TransitStops500m = 0, TransitStops1km = 0 → mobility score MUST be <= 40
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            PoiData = new PoiData { TransitStops500m = 0, TransitStops1km = 0, Pois2km = 2 },
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.PoiData!.TransitStops1km == 0),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(mobility: 30));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "mobility");
        dim.Score.ShouldNotBeNull();
        dim.Score!.Value.ShouldBeLessThanOrEqualTo(40);
    }

    // ── Infrastructure dimension ─────────────────────────────────────────────

    [Fact(Skip = "Red phase — InfrastructureRules not implemented yet (T030)")]
    public void Infrastructure_HospitalNearby_ScoresHigh()
    {
        // HospitalsWithin2km >= 1 → infrastructure score MUST be >= 100
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            HealthData = new HealthData { HospitalsWithin2km = 1, ClinicsWith2km = 3, EmergencyUnits2km = 1 },
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.HealthData!.HospitalsWithin2km >= 1),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(infrastructure: 140));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "infrastructure");
        dim.Score.ShouldNotBeNull();
        dim.Score!.Value.ShouldBeGreaterThanOrEqualTo(100);
    }

    // ── Environment dimension ────────────────────────────────────────────────

    [Fact(Skip = "Red phase — EnvironmentRules not implemented yet (T031)")]
    public void Environment_NoFloodRisk_Scores200()
    {
        // FloodRisk = null → environment score MUST be exactly 200
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            FloodRisk = new FloodRiskData { RiskLevel = null },
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.FloodRisk!.RiskLevel == null),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(environment: 200));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "environment");
        dim.Score.ShouldNotBeNull();
        dim.Score.ShouldBe(200);
    }

    [Fact(Skip = "Red phase — EnvironmentRules not implemented yet (T031)")]
    public void Environment_CriticalFloodRisk_Scores0()
    {
        // FloodRisk.RiskLevel = "critical" → environment score MUST be exactly 0
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            FloodRisk = new FloodRiskData { RiskLevel = "critical", DistanceMetres = 0 },
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.FloodRisk!.RiskLevel == "critical"),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(environment: 0));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "environment");
        dim.Score.ShouldNotBeNull();
        dim.Score.ShouldBe(0);
    }

    // ── Appreciation dimension ───────────────────────────────────────────────

    [Fact(Skip = "Red phase — AppreciationRules not implemented yet (T032)")]
    public void Appreciation_PositiveCagr_ScoresHigh()
    {
        // IptuData shows positive valuation growth → appreciation score MUST be >= 120
        // ValorVenal increased from prior year — inferred by engine from historical data
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            IptuData = new PropertyTaxData { ValorVenal = 950_000m, ZoningClass = "ZM-3a" },
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.IptuData != null),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(appreciation: 130));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "appreciation");
        dim.Score.ShouldNotBeNull();
        dim.Score!.Value.ShouldBeGreaterThanOrEqualTo(120);
    }

    // ── Urban context dimension ──────────────────────────────────────────────

    [Fact(Skip = "Red phase — UrbanContextRules not implemented yet (T033)")]
    public void UrbanContext_HighIncome_ScoresHigh()
    {
        // CensusData.MedianIncomeGroup >= 8 (out of 10) → urban_context score MUST be >= 130
        // Note: tasks.md uses 1–10 scale; PropertyProfile.CensusData uses 1–5 scale.
        // Mapping: tasks group 8+ ≈ domain group 4+ (top 40%)
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            CensusData = new CensusData { MedianIncomeGroup = 5, PopulationDensity = 12000 },
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Is<PropertyProfile>(p => p.CensusData!.MedianIncomeGroup >= 4),
                       Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(urbanContext: 150));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        var dim = result.DimensionScores.Single(d => d.Dimension == "urban_context");
        dim.Score.ShouldNotBeNull();
        dim.Score!.Value.ShouldBeGreaterThanOrEqualTo(130);
    }

    // ── Composite scoring ────────────────────────────────────────────────────

    [Fact(Skip = "Red phase — PropertyAnalysisEngine not implemented yet (T034)")]
    public void Engine_CompositeScore_IsSumOfAvailableDimensions()
    {
        // composite = sum of available dimension scores
        // max = 200 × number of available dimensions
        var profile = new PropertyProfile
        {
            Address = MakeAddress(),
            ProvidersUnavailable = []
        };

        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Any<PropertyProfile>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(
                  security: 118, mobility: 185, infrastructure: 162,
                  environment: 95, appreciation: 104, urbanContext: 60));

        var result = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), null);

        result.CompositeScore.ShouldBe(724);
        result.CompositeMax.ShouldBe(1200);
    }

    [Fact(Skip = "Red phase — PropertyAnalysisEngine not implemented yet (T034)")]
    public void Engine_Grade_IsComputedFromCompositePercentage()
    {
        // 724 / 1000 = 72.4% → B+ (70–79%)
        // Grade mapping from contracts/analyze-endpoint.md
        var engine = Substitute.For<IPropertyAnalysisEngine>();
        engine.Analyze(Arg.Any<PropertyProfile>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>())
              .Returns(MakeAnalysis(
                  security: 118, mobility: 185, infrastructure: 162,
                  environment: 95, appreciation: 104, urbanContext: 60) with { Grade = "B+" });

        var result = engine.Analyze(
            new PropertyProfile { Address = MakeAddress(), ProvidersUnavailable = [] },
            Guid.NewGuid(), Guid.NewGuid(), null);

        result.Grade.ShouldBe("B+");
    }
}
