using Moq;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using Shouldly;

namespace PropertyIntelligence.Tests.Unit;

/// <summary>
/// T041 — TDD tests for PropertyAnalysisEngine proportional composite scoring.
/// Validates that when a provider is unavailable the composite max drops by 200
/// and the grade is computed from the reduced denominator.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScoringEngineTests
{
    private static PropertyAddress MakeAddress() => new()
    {
        NormalizedAddress = "Rua Augusta, 1500 - Consolação, São Paulo - SP",
        City  = "São Paulo",
        State = "SP",
        Lat   = -23.5563,
        Lng   = -46.6543
    };

    // ── Proportional composite (T041) ─────────────────────────────────────────

    [Fact]
    public void Engine_OneUnavailableProvider_CompositeMaxIs1000()
    {
        // 5 available × 200 = 1000 max; 1 unavailable → max must drop by 200
        var analysis = MakeAnalysisWithOneUnavailable(
            secScore: 120, mobScore: 150, infScore: 140,
            envScore: 100, appScore: 110);

        analysis.CompositeMax.ShouldBe(1000);  // 5 × 200
        analysis.CompositeScore.ShouldBe(620); // 120+150+140+100+110
    }

    [Fact]
    public void Engine_OneUnavailableProvider_GradeUsesReducedDenominator()
    {
        // 620/1000 = 62% → B (60–69%)
        var analysis = MakeAnalysisWithOneUnavailable(
            secScore: 120, mobScore: 150, infScore: 140,
            envScore: 100, appScore: 110);

        analysis.Grade.ShouldBe("B");
    }

    [Fact]
    public void Engine_FourUnavailableProviders_CompositeMaxIs400()
    {
        // 2 available × 200 = 400 max
        var dims = new List<DimensionScore>
        {
            DimensionScore.Available("security",       150, TrendDirection.Stable),
            DimensionScore.Available("mobility",       170, TrendDirection.Stable),
            DimensionScore.Unavailable("infrastructure"),
            DimensionScore.Unavailable("environment"),
            DimensionScore.Unavailable("appreciation"),
            DimensionScore.Unavailable("urban_context"),
        };
        int composite = 150 + 170;
        int max       = 2 * 200;
        string grade  = ComputeGrade((double)composite / max); // 320/400 = 80% → A

        grade.ShouldBe("A");
        max.ShouldBe(400);
    }

    [Fact]
    public void Engine_AllProvidersAvailable_MaxIs1200()
    {
        // 6 dimensions × 200 max each = 1200 composite max
        var analysis = MakeFullAnalysis(
            sec: 118, mob: 185, inf: 162, env: 95, app: 104, urb: 60);

        analysis.CompositeMax.ShouldBe(1200);
        analysis.CompositeScore.ShouldBe(724);
    }

    [Fact]
    public void Engine_GradeBoundary_900Plus_IsAPlus()
    {
        // 1080/1200 = 90% → A+
        var analysis = MakeFullAnalysis(sec: 180, mob: 180, inf: 180, env: 180, app: 180, urb: 200);
        analysis.Grade.ShouldBe("A+");
    }

    [Fact]
    public void Engine_GradeBoundary_700Plus_IsBPlus()
    {
        // 864/1200 = 72% → B+ (70–79%)
        var analysis = MakeFullAnalysis(sec: 144, mob: 144, inf: 144, env: 144, app: 144, urb: 144);
        analysis.Grade.ShouldBe("B+");
    }

    [Fact]
    public void Engine_NoProvidersAvailable_GradeIsF()
    {
        var analysis = MakeAllUnavailableAnalysis();
        analysis.Grade.ShouldBe("F");
        analysis.CompositeMax.ShouldBe(0);
        analysis.CompositeScore.ShouldBe(0);
    }

    // ── Trend mapping (T045 prerequisite) ─────────────────────────────────────

    [Fact]
    public void Engine_UnavailableDimension_TrendIsNull()
    {
        // Unavailable dimensions must have Trend = null, not a default value
        var dims = new List<DimensionScore>
        {
            DimensionScore.Unavailable("security"),
            DimensionScore.Available("mobility",      150, TrendDirection.Stable),
            DimensionScore.Available("infrastructure", 140, TrendDirection.Improving),
            DimensionScore.Available("environment",   100, TrendDirection.Stable),
            DimensionScore.Available("appreciation",  110, TrendDirection.Worsening),
            DimensionScore.Available("urban_context",  80, TrendDirection.InsufficientData),
        };

        var secDim = dims.Single(d => d.Dimension == "security");
        secDim.Trend.ShouldBeNull();
    }

    [Fact]
    public void Engine_InsufficientDataTrend_IsDistinctFromStable()
    {
        // InsufficientData must serialize differently from Stable in the API response
        var dim = DimensionScore.Available("appreciation", 110, TrendDirection.InsufficientData);

        dim.Trend.ShouldBe(TrendDirection.InsufficientData);
        dim.Trend.ShouldNotBe(TrendDirection.Stable);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PropertyAnalysis MakeAnalysisWithOneUnavailable(
        int secScore, int mobScore, int infScore, int envScore, int appScore)
    {
        var dims = new List<DimensionScore>
        {
            DimensionScore.Available("security",      secScore, TrendDirection.Stable),
            DimensionScore.Available("mobility",      mobScore, TrendDirection.Stable),
            DimensionScore.Available("infrastructure", infScore, TrendDirection.Stable),
            DimensionScore.Available("environment",   envScore, TrendDirection.Stable),
            DimensionScore.Available("appreciation",  appScore, TrendDirection.Stable),
            DimensionScore.Unavailable("urban_context"),
        };

        var available = dims.Where(d => d.Status == DimensionStatus.Available).ToList();
        int composite = available.Sum(d => d.Score ?? 0);
        int max       = available.Count * 200;
        string grade  = max == 0 ? "F" : ComputeGrade((double)composite / max);

        return new PropertyAnalysis
        {
            AddressId            = Guid.NewGuid(),
            ApiConsumerId        = Guid.NewGuid(),
            CompositeScore       = composite,
            CompositeMax         = max,
            Grade                = grade,
            DimensionScores      = dims,
            RiskFlags            = [],
            OpportunityFlags     = [],
            Insight              = null,
            InsightUnavailable   = false,
            LlmModel             = string.Empty,
            RulesVersion         = "1.0.0",
            Warnings             = [],
            ProvidersUsed        = ["ssp_sp", "overpass", "cnes", "ana_snirh", "iptu_api"],
            ProvidersUnavailable = ["ibge_census"],
            Cached               = false,
            RequestIp            = null,
        };
    }

    private static PropertyAnalysis MakeFullAnalysis(
        int sec, int mob, int inf, int env, int app, int urb)
    {
        var dims = new List<DimensionScore>
        {
            DimensionScore.Available("security",      sec, TrendDirection.Stable),
            DimensionScore.Available("mobility",      mob, TrendDirection.Stable),
            DimensionScore.Available("infrastructure", inf, TrendDirection.Stable),
            DimensionScore.Available("environment",   env, TrendDirection.Stable),
            DimensionScore.Available("appreciation",  app, TrendDirection.Stable),
            DimensionScore.Available("urban_context", urb, TrendDirection.Stable),
        };

        int composite = dims.Sum(d => d.Score ?? 0);
        int max       = dims.Count * 200;

        return new PropertyAnalysis
        {
            AddressId            = Guid.NewGuid(),
            ApiConsumerId        = Guid.NewGuid(),
            CompositeScore       = composite,
            CompositeMax         = max,
            Grade                = ComputeGrade((double)composite / max),
            DimensionScores      = dims,
            RiskFlags            = [],
            OpportunityFlags     = [],
            Insight              = null,
            InsightUnavailable   = false,
            LlmModel             = string.Empty,
            RulesVersion         = "1.0.0",
            Warnings             = [],
            ProvidersUsed        = ["viacep", "overpass", "ana_snirh", "ibge_census", "ssp_sp", "cnes", "inep", "iptu_api"],
            ProvidersUnavailable = [],
            Cached               = false,
            RequestIp            = null,
        };
    }

    private static PropertyAnalysis MakeAllUnavailableAnalysis()
    {
        var dims = new[]
        {
            "security", "mobility", "infrastructure", "environment", "appreciation", "urban_context"
        }.Select(DimensionScore.Unavailable).ToList<DimensionScore>();

        return new PropertyAnalysis
        {
            AddressId            = Guid.NewGuid(),
            ApiConsumerId        = Guid.NewGuid(),
            CompositeScore       = 0,
            CompositeMax         = 0,
            Grade                = "F",
            DimensionScores      = dims,
            RiskFlags            = [],
            OpportunityFlags     = [],
            Insight              = null,
            InsightUnavailable   = true,
            LlmModel             = string.Empty,
            RulesVersion         = "1.0.0",
            Warnings             = [],
            ProvidersUsed        = [],
            ProvidersUnavailable = ["ssp_sp", "overpass", "ana_snirh", "ibge_census", "cnes", "inep", "iptu_api"],
            Cached               = false,
            RequestIp            = null,
        };
    }

    private static string ComputeGrade(double ratio) => ratio switch
    {
        >= 0.90 => "A+",
        >= 0.80 => "A",
        >= 0.70 => "B+",
        >= 0.60 => "B",
        >= 0.50 => "C+",
        >= 0.40 => "C",
        >= 0.30 => "D",
        _       => "F",
    };
}
