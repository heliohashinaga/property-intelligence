using System.Reflection;
using NRules;
using NRules.Fluent;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules;

/// <summary>
/// NRules-backed implementation of <see cref="IPropertyAnalysisEngine"/>.
/// Creates a fresh session per request, asserts a <see cref="PropertyFact"/>,
/// fires all rules, and aggregates <see cref="ScoringFact"/> results into a
/// <see cref="PropertyAnalysis"/>.
/// </summary>
public sealed class PropertyAnalysisEngine : IPropertyAnalysisEngine
{
    /// <inheritdoc/>
    public const string RulesVersionConst = "1.0.0";

    /// <inheritdoc/>
    public string RulesVersion => RulesVersionConst;

    private static readonly string[] Dimensions =
        ["security", "mobility", "infrastructure", "environment", "appreciation", "urban_context"];

    /// <summary>Provider name → dimension mapping for warning generation.</summary>
    private static readonly IReadOnlyDictionary<string, string> ProviderToDimension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ssp_sp"]     = "security",
            ["overpass"]   = "mobility",
            ["cnes"]       = "infrastructure",
            ["inep"]       = "infrastructure",
            ["ana_snirh"]  = "environment",
            ["iptu_api"]   = "appreciation",
            ["ibge_census"] = "urban_context",
        };

    private readonly ISessionFactory _sessionFactory;

    public PropertyAnalysisEngine(ISessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    /// <inheritdoc/>
    public PropertyAnalysis Analyze(
        PropertyProfile profile,
        Guid addressId,
        Guid apiConsumerId,
        string? requestIp)
    {
        // ── 1. Create a fresh NRules session ─────────────────────────────────
        var session = _sessionFactory.CreateSession();

        // ── 2. Insert the property fact ──────────────────────────────────────
        session.Insert(new PropertyFact { Profile = profile });

        // ── 3. Fire all rules ────────────────────────────────────────────────
        session.Fire();

        // ── 4. Collect scoring facts emitted by rules ────────────────────────
        var scoringFacts = session.Query<ScoringFact>().ToList();

        // ── 5. Aggregate per dimension (cap each at 200) ─────────────────────
        var dimensionScores = Dimensions.Select(dim =>
        {
            var facts = scoringFacts.Where(f => f.Dimension == dim).ToList();
            if (facts.Count == 0)
                return DimensionScore.Unavailable(dim);

            var total = Math.Min(200, facts.Sum(f => f.Points));
            // T045: use provider-sourced trend from PropertyProfile
            var trend = ResolveProviderTrend(dim, profile);
            return DimensionScore.Available(dim, total, trend);
        }).ToList();

        // ── 6. Composite score and denominator ───────────────────────────────
        var available = dimensionScores
            .Where(d => d.Status == DimensionStatus.Available)
            .ToList();

        int composite = available.Sum(d => d.Score ?? 0);
        int max       = available.Count * 200;

        // ── 7. Grade ─────────────────────────────────────────────────────────
        string grade = max == 0 ? "F" : ComputeGrade((double)composite / max);

        // ── 8. Warnings for unavailable providers ────────────────────────────
        var warnings = profile.ProvidersUnavailable
            .Where(p => ProviderToDimension.ContainsKey(p))
            .Select(p => AnalysisWarning.ProviderUnavailable(p, ProviderToDimension[p]))
            .ToList<AnalysisWarning>();

        // ── 9. Risk / opportunity flags ──────────────────────────────────────
        var riskFlags        = BuildRiskFlags(profile, dimensionScores);
        var opportunityFlags = BuildOpportunityFlags(profile);

        // ── 10. Build the append-only audit record ───────────────────────────
        return new PropertyAnalysis
        {
            AddressId            = addressId,
            ApiConsumerId        = apiConsumerId,
            CompositeScore       = composite,
            CompositeMax         = max,
            Grade                = grade,
            DimensionScores      = dimensionScores,
            RiskFlags            = riskFlags,
            OpportunityFlags     = opportunityFlags,
            Insight              = null,          // filled by LlmExplainabilityService
            InsightUnavailable   = false,
            LlmModel             = string.Empty,  // filled by AnalyzeEndpoint
            RulesVersion         = RulesVersionConst,
            Warnings             = warnings,
            ProvidersUsed        = GetProvidersUsed(profile),
            ProvidersUnavailable = profile.ProvidersUnavailable,
            Cached               = profile.AllFromCache,
            RequestIp            = requestIp,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>T045 — resolves trend for a dimension from provider-sourced data in the PropertyProfile.</summary>
    private static TrendDirection ResolveProviderTrend(string dimension, PropertyProfile profile) =>
        dimension switch
        {
            "security"       => profile.CrimeData?.SecurityTrend      ?? TrendDirection.InsufficientData,
            "mobility"       => profile.PoiData?.MobilityTrend         ?? TrendDirection.InsufficientData,
            "infrastructure" => ResolveInfrastructureTrend(profile),
            "environment"    => profile.FloodRisk?.Trend               ?? TrendDirection.Stable,
            "appreciation"   => profile.IptuData?.AppreciationTrend    ?? TrendDirection.InsufficientData,
            "urban_context"  => profile.CensusData?.UrbanContextTrend  ?? TrendDirection.InsufficientData,
            _                => TrendDirection.InsufficientData,
        };

    private static TrendDirection ResolveInfrastructureTrend(PropertyProfile profile)
    {
        var healthTrend = profile.HealthData?.InfrastructureTrend;
        var schoolTrend = profile.SchoolData?.InfrastructureTrend;
        if (healthTrend is null && schoolTrend is null) return TrendDirection.InsufficientData;
        var trends = new[] { healthTrend, schoolTrend }.OfType<TrendDirection>().ToList();
        if (trends.Any(t => t == TrendDirection.Worsening))        return TrendDirection.Worsening;
        if (trends.Any(t => t == TrendDirection.Improving))        return TrendDirection.Improving;
        if (trends.Any(t => t == TrendDirection.InsufficientData)) return TrendDirection.InsufficientData;
        return TrendDirection.Stable;
    }

    /// <summary>Kept for ScoringFact-based trend strings emitted by NRules (legacy path).</summary>
    private static TrendDirection ResolveTrend(IReadOnlyList<ScoringFact> facts)
    {
        if (facts.Any(f => f.Trend == "worsening")) return TrendDirection.Worsening;
        if (facts.Any(f => f.Trend == "improving")) return TrendDirection.Improving;
        return TrendDirection.Stable;
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

    private static IReadOnlyList<string> GetProvidersUsed(PropertyProfile profile)
    {
        var list = new List<string>();
        if (profile.AddressInfo  != null) list.Add("viacep");
        if (profile.PoiData      != null) list.Add("overpass");
        if (profile.FloodRisk    != null) list.Add("ana_snirh");
        if (profile.CensusData   != null) list.Add("ibge_census");
        if (profile.CrimeData    != null) list.Add("ssp_sp");
        if (profile.HealthData   != null) list.Add("cnes");
        if (profile.SchoolData   != null) list.Add("inep");
        if (profile.IptuData     != null) list.Add("iptu_api");
        return list;
    }

    private static IReadOnlyList<string> BuildRiskFlags(
        PropertyProfile profile,
        IReadOnlyList<DimensionScore> scores)
    {
        var flags = new List<string>();

        if (profile.FloodRisk?.RiskLevel is "moderate")  flags.Add("moderate_flood_risk");
        if (profile.FloodRisk?.RiskLevel is "high")      flags.Add("high_flood_risk");
        if (profile.FloodRisk?.RiskLevel is "critical")  flags.Add("critical_flood_risk");

        // T046 — crime trend worsening (uses provider SecurityTrend, not YoY%)
        if (profile.CrimeData?.SecurityTrend == TrendDirection.Worsening)
            flags.Add("crime_trend_12m");

        // T046 — high crime area (score-based fallback)
        var secScore = scores.FirstOrDefault(d => d.Dimension == "security");
        if (secScore?.Score < 60) flags.Add("high_crime_area");

        // T046 — low mobility: score < 80 per task spec
        var mobScore = scores.FirstOrDefault(d => d.Dimension == "mobility");
        if (mobScore?.Status == DimensionStatus.Available && mobScore.Score < 80)
            flags.Add("low_mobility");

        // T046 — no hospital within 2km
        if (profile.HealthData?.HospitalsWithin2km == 0)
            flags.Add("no_hospital_2km");

        return flags;
    }

    private static IReadOnlyList<string> BuildOpportunityFlags(PropertyProfile profile)
    {
        var flags = new List<string>();

        // T047 — future metro station within 1km (Overpass construction=station tag)
        if (profile.PoiData is { HasFutureMetro: true })
            flags.Add("metro_expansion_nearby");

        // T047 — zoning allows high density
        if (profile.IptuData?.ZoningClass is "ZEU" or "ZOE" or "ZC" or "ZM")
            flags.Add("zoning_upscale");

        // T047 — appreciation trend improving
        if (profile.IptuData?.AppreciationTrend == TrendDirection.Improving)
            flags.Add("appreciation_trend_up");

        // T047 — school excellence within 1km (IDEB >= 7.0)
        if (profile.SchoolData?.NearestSchoolIdeb >= 7.0)
            flags.Add("school_excellence_1km");

        return flags;
    }
}
