using FluentAssertions;
using NRules;
using NRules.Fluent;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Tests.Unit;

/// <summary>
/// T078 — red tests for the six NRules dimension rule classes.
/// These tests are intentionally added before the rule implementations (T028–T033)
/// and should fail until the rule types exist and produce the expected scores.
/// </summary>
public sealed class DimensionRulesTests
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
    public void SecurityRules_scores_at_least_160_when_crime_rate_is_below_50_per_100k()
    {
        var profile = BuildProfile(crimeData: new CrimeData
        {
            CrimeRatePer100k = 42,
            YoyChangePct = -8,
            SecurityTrend = TrendDirection.Improving,
        });

        var points = ExecuteDimension("SecurityRules", "security", profile);

        points.Should().BeGreaterThanOrEqualTo(160);
    }

    [Fact]
    public void SecurityRules_scores_at_most_40_when_crime_rate_is_above_300_per_100k()
    {
        var profile = BuildProfile(crimeData: new CrimeData
        {
            CrimeRatePer100k = 350,
            YoyChangePct = 12,
            SecurityTrend = TrendDirection.Worsening,
        });

        var points = ExecuteDimension("SecurityRules", "security", profile);

        points.Should().BeLessThanOrEqualTo(40);
    }

    [Fact]
    public void MobilityRules_scores_at_least_150_when_transit_access_is_strong_with_stops_within_500m()
    {
        var profile = BuildProfile(poiData: new PoiData
        {
            TransitStops500m = 12,
            TransitStops1km = 24,
            Pois2km = 180,
            Supermarkets1km = 4,
            Pharmacies1km = 5,
            Parks1km = 2,
            MobilityTrend = TrendDirection.Improving,
        });

        var points = ExecuteDimension("MobilityRules", "mobility", profile);

        points.Should().BeGreaterThanOrEqualTo(150);
    }

    [Fact]
    public void MobilityRules_scores_at_most_40_when_there_is_no_transit_access()
    {
        var profile = BuildProfile(poiData: new PoiData
        {
            TransitStops500m = 0,
            TransitStops1km = 0,
            Pois2km = 0,
            Supermarkets1km = 0,
            Pharmacies1km = 0,
            Parks1km = 0,
            MobilityTrend = TrendDirection.Stable,
        });

        var points = ExecuteDimension("MobilityRules", "mobility", profile);

        points.Should().BeLessThanOrEqualTo(40);
    }

    [Fact]
    public void InfrastructureRules_awards_at_least_100_when_a_hospital_exists_within_2km()
    {
        var profile = BuildProfile(
            healthData: new HealthData
            {
                HospitalsWithin2km = 1,
                ClinicsWith2km = 0,
                EmergencyUnits2km = 0,
                InfrastructureTrend = TrendDirection.Stable,
            },
            schoolData: new SchoolData
            {
                SchoolsWithin2km = 0,
                NearestSchoolIdeb = null,
                InfrastructureTrend = TrendDirection.Stable,
            });

        var points = ExecuteDimension("InfrastructureRules", "infrastructure", profile);

        points.Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void EnvironmentRules_returns_200_when_there_is_no_flood_risk()
    {
        var profile = BuildProfile(floodRiskData: new FloodRiskData
        {
            RiskLevel = null,
            DistanceMetres = null,
            Trend = TrendDirection.Stable,
        });

        var points = ExecuteDimension("EnvironmentRules", "environment", profile);

        points.Should().Be(200);
    }

    [Fact]
    public void EnvironmentRules_returns_0_when_flood_risk_is_critical()
    {
        var profile = BuildProfile(floodRiskData: new FloodRiskData
        {
            RiskLevel = "critical",
            DistanceMetres = 0,
            Trend = TrendDirection.Stable,
        });

        var points = ExecuteDimension("EnvironmentRules", "environment", profile);

        points.Should().Be(0);
    }

    [Fact]
    public void AppreciationRules_awards_at_least_120_for_permissive_zoning_with_positive_public_signal()
    {
        var profile = BuildProfile(iptuData: new IptuData
        {
            ValorVenal = 900000m,
            ZoningClass = "ZEU",
            AppreciationTrend = TrendDirection.Improving,
        });

        var points = ExecuteDimension("AppreciationRules", "appreciation", profile);

        points.Should().BeGreaterThanOrEqualTo(120);
    }

    [Fact]
    public void UrbanContextRules_awards_at_least_130_for_income_group_8_or_higher()
    {
        var profile = BuildProfile(censusData: new CensusData
        {
            MedianIncomeGroup = 8,
            PopulationDensity = 8500,
            WorkingAgePct = 70,
            UrbanContextTrend = TrendDirection.Improving,
        });

        var points = ExecuteDimension("UrbanContextRules", "urban_context", profile);

        points.Should().BeGreaterThanOrEqualTo(130);
    }

    private static int ExecuteDimension(string ruleTypeName, string dimension, PropertyProfile profile)
    {
        var session = CreateSession(ruleTypeName);
        session.Insert(new PropertyFact { Profile = profile });
        session.Fire();

        var facts = session.Query<ScoringFact>().ToList();
        facts.Should().NotBeEmpty($"{ruleTypeName} should emit at least one scoring fact for {dimension}");

        var matchingFacts = facts.Where(fact => string.Equals(fact.Dimension, dimension, StringComparison.OrdinalIgnoreCase)).ToList();
        matchingFacts.Should().NotBeEmpty($"{ruleTypeName} should emit scoring facts for the {dimension} dimension");

        return matchingFacts.Sum(fact => fact.Points);
    }

    private static ISession CreateSession(string ruleTypeName)
    {
        var rulesAssembly = typeof(PropertyFact).Assembly;
        var fullTypeName = $"PropertyIntelligence.Rules.Dimensions.{ruleTypeName}";
        var ruleType = rulesAssembly.GetType(fullTypeName);

        ruleType.Should().NotBeNull($"T078 expects rule type {fullTypeName} to exist in the rules assembly");

        var repository = new RuleRepository();
        repository.Load(x => x.From(ruleType!));
        var factory = repository.Compile();
        return factory.CreateSession();
    }

    private static PropertyProfile BuildProfile(
        PoiData? poiData = null,
        FloodRiskData? floodRiskData = null,
        CensusData? censusData = null,
        CrimeData? crimeData = null,
        HealthData? healthData = null,
        SchoolData? schoolData = null,
        IptuData? iptuData = null)
    {
        return new PropertyProfile
        {
            Address = SampleAddress,
            AddressInfo = new AddressInfo
            {
                NormalizedAddress = SampleAddress.NormalizedAddress,
                StreetName = SampleAddress.StreetName,
                StreetNumber = SampleAddress.StreetNumber,
                Neighborhood = SampleAddress.Neighborhood,
                City = SampleAddress.City,
                State = SampleAddress.State,
                PostalCode = SampleAddress.PostalCode,
                Lat = SampleAddress.Lat,
                Lng = SampleAddress.Lng,
            },
            PoiData = poiData,
            FloodRisk = floodRiskData,
            CensusData = censusData,
            CrimeData = crimeData,
            HealthData = healthData,
            SchoolData = schoolData,
            IptuData = iptuData,
            ProvidersUnavailable = [],
            AllFromCache = false,
        };
    }
}
