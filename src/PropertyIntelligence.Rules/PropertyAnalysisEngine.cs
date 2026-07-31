using NRules;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules;

/// <summary>
/// Runs the compiled NRules session over a fully enriched property profile and
/// materializes a <see cref="PropertyAnalysis"/> audit record.
/// </summary>
public sealed class PropertyAnalysisEngine : IPropertyAnalysisEngine
{
    private static readonly string[] OrderedDimensions =
    [
        "security",
        "mobility",
        "infrastructure",
        "environment",
        "appreciation",
        "urban_context",
    ];

    private readonly ISessionFactory _sessionFactory;

    public PropertyAnalysisEngine(ISessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public string RulesVersion => "1.0.0";

    public PropertyAnalysis Analyze(
        PropertyProfile profile,
        Guid addressId,
        Guid apiConsumerId,
        string? requestIp)
    {
        var session = _sessionFactory.CreateSession();
        session.Insert(new PropertyFact { Profile = profile });
        session.Fire();

        var scoringFacts = session.Query<ScoringFact>().ToList();
        var scoreByDimension = scoringFacts
            .GroupBy(fact => fact.Dimension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => Math.Min(200, group.Sum(fact => fact.Points)),
                StringComparer.OrdinalIgnoreCase);

        var dimensionScores = OrderedDimensions
            .Select(dimension => CreateDimensionScore(dimension, profile, scoreByDimension))
            .ToArray();

        var availableDimensions = dimensionScores
            .Where(score => score.Status == DimensionStatus.Available)
            .ToArray();

        var compositeScore = availableDimensions.Sum(score => score.Score ?? 0);
        var compositeMax = availableDimensions.Sum(score => score.Max);
        var grade = ComputeGrade(compositeScore, compositeMax);

        return new PropertyAnalysis
        {
            AddressId = addressId,
            ApiConsumerId = apiConsumerId,
            CompositeScore = compositeScore,
            CompositeMax = compositeMax,
            Grade = grade,
            DimensionScores = dimensionScores,
            RiskFlags = BuildRiskFlags(profile, scoreByDimension),
            OpportunityFlags = BuildOpportunityFlags(profile),
            Insight = null,
            InsightUnavailable = true,
            LlmModel = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "unknown",
            RulesVersion = RulesVersion,
            Warnings = profile.Warnings,
            ProvidersUsed = profile.ProvidersUsed,
            ProvidersUnavailable = profile.ProvidersUnavailable,
            Cached = profile.AllFromCache,
            RequestIp = requestIp,
        };
    }

    private static IReadOnlyList<string> BuildRiskFlags(
        PropertyProfile profile,
        IReadOnlyDictionary<string, int> scoreByDimension)
    {
        var flags = new List<string>();

        // Flood risk: moderate → "moderate_flood_risk"; high/critical → "high_flood_risk"
        if (profile.FloodRisk?.RiskLevel is "critical" or "high")
        {
            flags.Add("high_flood_risk");
        }
        else if (profile.FloodRisk?.RiskLevel is "moderate")
        {
            flags.Add("moderate_flood_risk");
        }

        // Security trend worsening → "crime_trend_12m"
        if (profile.CrimeData?.SecurityTrend == TrendDirection.Worsening)
        {
            flags.Add("crime_trend_12m");
        }

        // Low mobility score
        if (scoreByDimension.TryGetValue("mobility", out var mobilityScore) && mobilityScore < 80)
        {
            flags.Add("low_mobility");
        }

        // No hospital within 2km (only flagged when health data was available)
        if (profile.HealthData is not null && profile.HealthData.HospitalsWithin2km == 0)
        {
            flags.Add("no_hospital_2km");
        }

        return flags;
    }

    private static IReadOnlyList<string> BuildOpportunityFlags(PropertyProfile profile)
    {
        var flags = new List<string>();

        // Confirmed future transit project within 1 km
        if (profile.IptuData?.FutureTransitDistanceMetres is <= 1000)
        {
            flags.Add("metro_expansion_nearby");
        }

        // Zoning allows higher density (permissiveness score ≥ 3 out of 5)
        if (profile.IptuData?.ZoningPermissivenessScore >= 3)
        {
            flags.Add("zoning_upscale");
        }

        // Appreciation trend improving
        if (profile.IptuData?.AppreciationTrend == TrendDirection.Improving)
        {
            flags.Add("appreciation_trend_up");
        }

        // Nearest school IDEB ≥ 7.0 within 1 km
        if (profile.SchoolData?.NearestSchoolIdeb >= 7.0)
        {
            flags.Add("school_excellence_1km");
        }

        return flags;
    }

    private static DimensionScore CreateDimensionScore(
        string dimension,
        PropertyProfile profile,
        IReadOnlyDictionary<string, int> scoreByDimension)
    {
        var score = scoreByDimension.TryGetValue(dimension, out var value) ? value : 0;

        return dimension switch
        {
            "security" => profile.CrimeData is null
                ? DimensionScore.Unavailable(dimension)
                : DimensionScore.Available(dimension, score, profile.CrimeData.SecurityTrend ?? TrendDirection.Stable),

            "mobility" => profile.PoiData is null
                ? DimensionScore.Unavailable(dimension)
                : DimensionScore.Available(dimension, score, profile.PoiData.MobilityTrend ?? TrendDirection.Stable),

            "infrastructure" => profile.HealthData is null && profile.SchoolData is null
                ? DimensionScore.Unavailable(dimension)
                : DimensionScore.Available(
                    dimension,
                    score,
                    profile.HealthData?.InfrastructureTrend
                    ?? profile.SchoolData?.InfrastructureTrend
                    ?? TrendDirection.Stable),

            "environment" => profile.FloodRisk is null
                ? DimensionScore.Unavailable(dimension)
                : DimensionScore.Available(dimension, score, profile.FloodRisk.Trend),

            "appreciation" => profile.IptuData is null
                ? DimensionScore.Unavailable(dimension)
                : DimensionScore.Available(dimension, score, profile.IptuData.AppreciationTrend ?? TrendDirection.Stable),

            "urban_context" => profile.CensusData is null
                ? DimensionScore.Unavailable(dimension)
                : DimensionScore.Available(dimension, score, profile.CensusData.UrbanContextTrend ?? TrendDirection.Stable),

            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Unsupported dimension."),
        };
    }

    private static string ComputeGrade(int compositeScore, int compositeMax)
    {
        if (compositeMax <= 0)
        {
            return "F";
        }

        var percentage = compositeScore / (double)compositeMax;

        if (percentage >= 0.90d) return "A+";
        if (percentage >= 0.80d) return "A";
        if (percentage >= 0.70d) return "B+";
        if (percentage >= 0.60d) return "B";
        if (percentage >= 0.50d) return "C+";
        if (percentage >= 0.40d) return "C";
        if (percentage >= 0.30d) return "D";
        return "F";
    }
}
