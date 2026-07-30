using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Mock;
using Xunit;

namespace PropertyIntelligence.Tests.Contract;

/// <summary>
/// Contract tests for the real public-data providers planned in T095-T100.
///
/// These tests keep deterministic source fixtures in-repo and establish the
/// required provider types + domain payload contracts before the providers are
/// implemented. They are expected to fail until those concrete providers exist.
/// </summary>
public sealed class PublicDataProvidersTests
{
    public static TheoryData<PublicDataProviderContractCase> ProviderContracts => new()
    {
        new PublicDataProviderContractCase(
            "PropertyIntelligence.Providers.Ana.AnaFloodRiskProvider",
            typeof(IDataProvider<FloodRiskData>),
            "ana_snirh",
            TimeSpan.FromDays(30),
            "Fixtures/PublicData/Ana/flood_risk_happy.json",
            AssertAnaFixture),
        new PublicDataProviderContractCase(
            "PropertyIntelligence.Providers.Ibge.IbgeCensusProvider",
            typeof(IDataProvider<CensusData>),
            "ibge_census",
            TimeSpan.FromDays(30),
            "Fixtures/PublicData/Ibge/census_sector_happy.json",
            AssertIbgeFixture),
        new PublicDataProviderContractCase(
            "PropertyIntelligence.Providers.Crime.CrimeDataProvider",
            typeof(IDataProvider<CrimeData>),
            "ssp_sp",
            TimeSpan.FromHours(24),
            "Fixtures/PublicData/Crime/crime_trend_happy.json",
            AssertCrimeFixture),
        new PublicDataProviderContractCase(
            "PropertyIntelligence.Providers.Cnes.CnesHealthProvider",
            typeof(IDataProvider<HealthData>),
            "cnes",
            TimeSpan.FromDays(7),
            "Fixtures/PublicData/Cnes/health_facilities_happy.json",
            AssertCnesFixture),
        new PublicDataProviderContractCase(
            "PropertyIntelligence.Providers.Inep.InepSchoolProvider",
            typeof(IDataProvider<SchoolData>),
            "inep",
            TimeSpan.FromDays(30),
            "Fixtures/PublicData/Inep/schools_happy.json",
            AssertInepFixture),
        new PublicDataProviderContractCase(
            "PropertyIntelligence.Providers.GeoSampa.GeoSampaZoneamentoProvider",
            typeof(IDataProvider<IptuData>),
            "geosampa_zoneamento",
            TimeSpan.FromDays(30),
            "Fixtures/PublicData/GeoSampa/zoneamento_happy.json",
            AssertGeoSampaFixture),
    };

    [Theory]
    [MemberData(nameof(ProviderContracts))]
    public async Task PublicDataProviderContractFixturesRemainDeterministicAndConcreteProviderTypeMustExist(
        PublicDataProviderContractCase contractCase)
    {
        var fixtureJson = await File.ReadAllTextAsync(contractCase.FixtureRelativePath);
        fixtureJson.Should().NotBeNullOrWhiteSpace();

        using var fixture = JsonDocument.Parse(fixtureJson);
        fixture.RootElement.GetProperty("provider").GetString().Should().Be(contractCase.ProviderName);
        AssertFixtureTtl(fixture.RootElement, contractCase.ExpectedCacheTtl);
        contractCase.AssertFixture(fixture.RootElement);

        var providerType = typeof(MockAddressProvider).Assembly.GetType(contractCase.FullTypeName);

        providerType.Should().NotBeNull(
            $"expected provider type '{contractCase.FullTypeName}' to exist and satisfy the fixture contract at '{contractCase.FixtureRelativePath}'");

        if (providerType is null)
        {
            return;
        }

        contractCase.ExpectedInterfaceType.IsAssignableFrom(providerType).Should().BeTrue(
            $"{contractCase.FullTypeName} must implement {contractCase.ExpectedInterfaceType.Name}");
    }

    private static void AssertAnaFixture(JsonElement root)
    {
        root.GetProperty("provider").GetString().Should().Be("ana_snirh");
        root.GetProperty("ttl_days").GetInt32().Should().Be(30);

        var zones = root.GetProperty("zones");
        zones.GetArrayLength().Should().BeGreaterThan(1);
        var firstZone = zones.EnumerateArray().First();
        firstZone.GetProperty("risk_level").GetString().Should().NotBeNullOrWhiteSpace();
        firstZone.GetProperty("distance_metres").ValueKind.Should().Be(JsonValueKind.Number);

        var expected = root.GetProperty("expected");
        expected.GetProperty("risk_level").GetString().Should().Be("high");
        expected.GetProperty("distance_metres").GetDouble().Should().Be(0d);
        expected.GetProperty("trend").GetString().Should().Be("Stable");
    }

    private static void AssertIbgeFixture(JsonElement root)
    {
        root.GetProperty("provider").GetString().Should().Be("ibge_census");
        root.GetProperty("ttl_days").GetInt32().Should().Be(30);

        var sector = root.GetProperty("sector");
        sector.GetProperty("sector_code").GetString().Should().NotBeNullOrWhiteSpace();
        sector.GetProperty("median_income_group").GetInt32().Should().BeInRange(1, 10);
        sector.GetProperty("population_density").ValueKind.Should().Be(JsonValueKind.Number);
        sector.GetProperty("working_age_pct").ValueKind.Should().Be(JsonValueKind.Number);

        var expected = root.GetProperty("expected");
        expected.GetProperty("median_income_group").GetInt32().Should().Be(8);
        expected.GetProperty("population_density").GetDouble().Should().BeApproximately(12345.67d, 0.01d);
        expected.GetProperty("working_age_pct").GetDouble().Should().BeApproximately(69.4d, 0.01d);
    }

    private static void AssertCrimeFixture(JsonElement root)
    {
        root.GetProperty("provider").GetString().Should().Be("ssp_sp");
        root.GetProperty("ttl_hours").GetInt32().Should().Be(24);

        var records = root.GetProperty("records");
        records.GetArrayLength().Should().BeGreaterThan(1);
        var firstRecord = records.EnumerateArray().First();
        firstRecord.GetProperty("crime_type").GetString().Should().NotBeNullOrWhiteSpace();
        firstRecord.GetProperty("per_100k").ValueKind.Should().Be(JsonValueKind.Number);

        var expected = root.GetProperty("expected");
        expected.GetProperty("crime_rate_per_100k").GetDouble().Should().BeApproximately(845.3d, 0.01d);
        expected.GetProperty("yoy_change_pct").GetDouble().Should().BeApproximately(-3.1d, 0.01d);
    }

    private static void AssertCnesFixture(JsonElement root)
    {
        root.GetProperty("provider").GetString().Should().Be("cnes");
        root.GetProperty("ttl_days").GetInt32().Should().Be(7);

        var facilities = root.GetProperty("facilities");
        facilities.GetArrayLength().Should().BeGreaterThan(1);
        var firstFacility = facilities.EnumerateArray().First();
        firstFacility.GetProperty("facility_type").GetString().Should().NotBeNullOrWhiteSpace();
        firstFacility.GetProperty("distance_metres").ValueKind.Should().Be(JsonValueKind.Number);

        var expected = root.GetProperty("expected");
        expected.GetProperty("hospitals_within_2km").GetInt32().Should().Be(2);
        expected.GetProperty("clinics_within_2km").GetInt32().Should().Be(4);
        expected.GetProperty("emergency_units_2km").GetInt32().Should().Be(1);
    }

    private static void AssertInepFixture(JsonElement root)
    {
        root.GetProperty("provider").GetString().Should().Be("inep");
        root.GetProperty("ttl_days").GetInt32().Should().Be(30);

        var schools = root.GetProperty("schools");
        schools.GetArrayLength().Should().BeGreaterThan(1);
        var firstSchool = schools.EnumerateArray().First();
        firstSchool.GetProperty("inep_code").GetString().Should().NotBeNullOrWhiteSpace();
        firstSchool.GetProperty("ideb_score").ValueKind.Should().Be(JsonValueKind.Number);

        var expected = root.GetProperty("expected");
        expected.GetProperty("schools_within_2km").GetInt32().Should().Be(3);
        expected.GetProperty("nearest_school_ideb").GetDouble().Should().BeApproximately(7.4d, 0.01d);
    }

    private static void AssertGeoSampaFixture(JsonElement root)
    {
        root.GetProperty("provider").GetString().Should().Be("geosampa_zoneamento");
        root.GetProperty("ttl_days").GetInt32().Should().Be(30);

        var zones = root.GetProperty("zones");
        zones.GetArrayLength().Should().BeGreaterThan(0);
        var firstZone = zones.EnumerateArray().First();
        firstZone.GetProperty("zone_code").GetString().Should().NotBeNullOrWhiteSpace();
        firstZone.GetProperty("permissiveness_score").ValueKind.Should().Be(JsonValueKind.Number);

        var expected = root.GetProperty("expected");
        expected.GetProperty("zoning_class").GetString().Should().Be("ZEU");
        expected.GetProperty("valor_venal").GetDecimal().Should().Be(1250000.00m);
    }

    private static void AssertFixtureTtl(JsonElement root, TimeSpan expectedCacheTtl)
    {
        if (root.TryGetProperty("ttl_days", out var ttlDays))
        {
            ttlDays.GetInt32().Should().Be((int)expectedCacheTtl.TotalDays);
            return;
        }

        if (root.TryGetProperty("ttl_hours", out var ttlHours))
        {
            ttlHours.GetInt32().Should().Be((int)expectedCacheTtl.TotalHours);
            return;
        }

        throw new InvalidOperationException("Fixture must declare ttl_days or ttl_hours.");
    }

    public sealed record PublicDataProviderContractCase(
        string FullTypeName,
        Type ExpectedInterfaceType,
        string ProviderName,
        TimeSpan ExpectedCacheTtl,
        string FixtureRelativePath,
        Action<JsonElement> AssertFixture);
}
