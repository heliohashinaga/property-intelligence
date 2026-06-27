using FluentAssertions;
using NRules;
using NRules.Fluent;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Rules;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Tests.Unit;

public sealed class ScoringEngineTests
{
    private static readonly PropertyAddress SampleAddress = new()
    {
        NormalizedAddress = "Rua Augusta, 1500 - Consolação, São Paulo - SP",
        StreetName = "Rua Augusta",
        StreetNumber = "1500",
        Neighborhood = "Consolação",
        City = "São Paulo",
        State = "SP",
        PostalCode = "01310100",
        Lat = -23.5563,
        Lng = -46.6543,
    };

    [Fact]
    public void Analyze_with_all_providers_available_computes_composite_and_max_and_grade()
    {
        // 1. Arrange
        var profile = new PropertyProfile
        {
            Address = SampleAddress,
            CrimeData = new CrimeData { CrimeRatePer100k = 42 }, // points = 180
            PoiData = new PoiData { TransitStops500m = 12 }, // points = 120
            HealthData = new HealthData { HospitalsWithin2km = 1 }, // points = 100
            FloodRisk = new FloodRiskData { RiskLevel = null }, // points = 200
            IptuData = new IptuData { ZoningClass = "ZEU", AppreciationTrend = TrendDirection.Improving }, // points = 100 + 0 + 40 = 140
            CensusData = new CensusData { MedianIncomeGroup = 8 }, // points = 130 + 0 = 130
            ProvidersUnavailable = [],
        };

        var sessionFactory = CompileSessionFactory();
        var engine = new PropertyAnalysisEngine(sessionFactory);

        // 2. Act
        var analysis = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1");

        // 3. Assert
        analysis.CompositeScore.Should().Be(180 + 120 + 100 + 200 + 140 + 130); // 870
        analysis.CompositeMax.Should().Be(1200);
        analysis.Grade.Should().Be("B+"); // 870 / 1200 = 72.5% -> B+

        analysis.DimensionScores.Should().HaveCount(6);
        analysis.DimensionScores.Should().ContainSingle(s => s.Dimension == "security" && s.Score == 180 && s.Status == DimensionStatus.Available);
        analysis.DimensionScores.Should().ContainSingle(s => s.Dimension == "mobility" && s.Score == 120 && s.Status == DimensionStatus.Available);
        analysis.DimensionScores.Should().ContainSingle(s => s.Dimension == "infrastructure" && s.Score == 100 && s.Status == DimensionStatus.Available);
        analysis.DimensionScores.Should().ContainSingle(s => s.Dimension == "environment" && s.Score == 200 && s.Status == DimensionStatus.Available);
        analysis.DimensionScores.Should().ContainSingle(s => s.Dimension == "appreciation" && s.Score == 140 && s.Status == DimensionStatus.Available);
        analysis.DimensionScores.Should().ContainSingle(s => s.Dimension == "urban_context" && s.Score == 130 && s.Status == DimensionStatus.Available);
    }

    [Fact]
    public void Analyze_with_unavailable_provider_reduces_max_score_and_omits_dimension()
    {
        // 1. Arrange
        var profile = new PropertyProfile
        {
            Address = SampleAddress,
            CrimeData = null, // Unavailable
            PoiData = new PoiData { TransitStops500m = 12 }, // points = 120
            HealthData = new HealthData { HospitalsWithin2km = 1 }, // points = 100
            FloodRisk = new FloodRiskData { RiskLevel = null }, // points = 200
            IptuData = new IptuData { ZoningClass = "ZEU", AppreciationTrend = TrendDirection.Improving }, // points = 140
            CensusData = new CensusData { MedianIncomeGroup = 8 }, // points = 130
            ProvidersUnavailable = ["ssp_sp"],
        };

        var sessionFactory = CompileSessionFactory();
        var engine = new PropertyAnalysisEngine(sessionFactory);

        // 2. Act
        var analysis = engine.Analyze(profile, Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1");

        // 3. Assert
        analysis.CompositeScore.Should().Be(120 + 100 + 200 + 140 + 130); // 690
        analysis.CompositeMax.Should().Be(1000);
        analysis.Grade.Should().Be("B"); // 690 / 1000 = 69% -> B

        analysis.DimensionScores.Should().HaveCount(6);
        analysis.DimensionScores.Should().ContainSingle(s => s.Dimension == "security" && s.Score == null && s.Status == DimensionStatus.Unavailable);
        analysis.DimensionScores.Should().ContainSingle(s => s.Dimension == "mobility" && s.Score == 120 && s.Status == DimensionStatus.Available);
    }

    private static ISessionFactory CompileSessionFactory()
    {
        var repository = new RuleRepository();
        repository.Load(x => x.From(typeof(PropertyFact).Assembly));
        return repository.Compile();
    }
}
