namespace PropertyIntelligence.Core.Domain;

// ── Provider data sub-records ─────────────────────────────────────────────────
// Each record mirrors exactly one IDataProvider<T>'s output.
// Nullable properties indicate data that wasn't available from the source.

/// <summary>ViaCEP — address resolution result (also carries geocode coordinates).</summary>
public sealed record AddressInfo
{
    public required string NormalizedAddress { get; init; }
    public string? StreetName               { get; init; }
    public string? StreetNumber             { get; init; }
    public string? Neighborhood             { get; init; }
    public required string City             { get; init; }
    public required string State            { get; init; }
    public string? PostalCode               { get; init; }
    public double? Lat                      { get; init; }
    public double? Lng                      { get; init; }
}

/// <summary>Overpass (OpenStreetMap) — point-of-interest counts around the address.</summary>
public sealed record PoiData
{
    /// <summary>Transit stops (bus, metro, train) within 500 m.</summary>
    public int TransitStops500m  { get; init; }
    /// <summary>Transit stops within 1 km.</summary>
    public int TransitStops1km   { get; init; }
    /// <summary>All POIs (amenity=*) within 2 km.</summary>
    public int Pois2km           { get; init; }
    /// <summary>Supermarkets / grocery stores within 1 km.</summary>
    public int Supermarkets1km   { get; init; }
    /// <summary>Pharmacies within 1 km.</summary>
    public int Pharmacies1km     { get; init; }
    /// <summary>Parks / green areas within 1 km.</summary>
    public int Parks1km          { get; init; }
    /// <summary>Trend direction populated in US2 (Phase 4). Null at MVP.</summary>
    public TrendDirection? MobilityTrend { get; init; }
}

/// <summary>ANA SNIRH — flood risk zone intersection result.</summary>
public sealed record FloodRiskData
{
    /// <summary>Highest risk level for any flood zone intersecting the address point. Null if no zone found.</summary>
    public string? RiskLevel    { get; init; }   // "low" | "moderate" | "high" | "critical"
    /// <summary>Distance in metres to the nearest flood zone boundary (0 if inside a zone).</summary>
    public double? DistanceMetres { get; init; }
    /// <summary>Always Stable at MVP; derived from snapshots in US2 (Phase 4).</summary>
    public TrendDirection Trend  { get; init; } = TrendDirection.Stable;
}

/// <summary>IBGE Censo 2022 — census sector socioeconomic data.</summary>
public sealed record CensusData
{
    /// <summary>Median income group (1–5 where 5 = highest). Null if sector not found.</summary>
    public int? MedianIncomeGroup      { get; init; }
    /// <summary>Population density (residents / km²).</summary>
    public double? PopulationDensity   { get; init; }
    /// <summary>Percentage of residents aged 15–64.</summary>
    public double? WorkingAgePct       { get; init; }
    /// <summary>Trend direction populated in US2 (Phase 4). Null at MVP.</summary>
    public TrendDirection? UrbanContextTrend { get; init; }
}

/// <summary>SSP-SP — crime statistics from the local PostgreSQL import.</summary>
public sealed record CrimeData
{
    /// <summary>Crime incidents per 100 k residents in the last 12 months.</summary>
    public double? CrimeRatePer100k { get; init; }
    /// <summary>Year-over-year change in crime rate (positive = increase).</summary>
    public double? YoyChangePct     { get; init; }
    /// <summary>Trend direction populated in US2 (Phase 4). Null at MVP.</summary>
    public TrendDirection? SecurityTrend { get; init; }
}

/// <summary>CNES / DataSUS — health facility counts near the address.</summary>
public sealed record HealthData
{
    /// <summary>Hospitals (CNES type HOSPITAL) within 2 km.</summary>
    public int HospitalsWithin2km    { get; init; }
    /// <summary>UBS / primary care units within 2 km.</summary>
    public int ClinicsWith2km        { get; init; }
    /// <summary>Emergency units (UPA/SAMU bases) within 2 km.</summary>
    public int EmergencyUnits2km     { get; init; }
    /// <summary>Trend direction populated in US2 (Phase 4). Null at MVP.</summary>
    public TrendDirection? InfrastructureTrend { get; init; }
}

/// <summary>INEP / IDEB — nearest public school data.</summary>
public sealed record SchoolData
{
    /// <summary>Number of public schools within 2 km.</summary>
    public int SchoolsWithin2km        { get; init; }
    /// <summary>Average IDEB score of the nearest school (0–10). Null if no school found.</summary>
    public double? NearestSchoolIdeb   { get; init; }
    /// <summary>Trend direction populated in US2 (Phase 4). Null at MVP.</summary>
    public TrendDirection? InfrastructureTrend { get; init; }
}

/// <summary>IPTU API — property valuation and zoning data.</summary>
public sealed record IptuData
{
    /// <summary>Most recent IPTU assessed value (valor venal) in BRL. Null if unavailable.</summary>
    public decimal? ValorVenal         { get; init; }
    /// <summary>Zoning classification, e.g. "ZM-1", "ZEU", "ZC". Null if unavailable.</summary>
    public string? ZoningClass         { get; init; }
    /// <summary>Trend direction populated in US2 (Phase 4). Null at MVP.</summary>
    public TrendDirection? AppreciationTrend { get; init; }
}

// ── PropertyProfile aggregate ─────────────────────────────────────────────────

/// <summary>
/// Aggregates the outputs of all eight data providers for a single address.
/// Passed to <see cref="Interfaces.IPropertyAnalysisEngine"/> for scoring
/// and to <see cref="Interfaces.IExplainabilityService"/> for insight generation.
/// </summary>
public sealed record PropertyProfile
{
    public required PropertyAddress Address { get; init; }

    // Individual provider results — null when provider was unavailable
    public AddressInfo?  AddressInfo  { get; init; }
    public PoiData?      PoiData      { get; init; }
    public FloodRiskData? FloodRisk   { get; init; }
    public CensusData?   CensusData   { get; init; }
    public CrimeData?    CrimeData    { get; init; }
    public HealthData?   HealthData   { get; init; }
    public SchoolData?   SchoolData   { get; init; }
    public IptuData?     IptuData     { get; init; }

    /// <summary>Provider names that failed or timed out during enrichment.</summary>
    public required IReadOnlyList<string> ProvidersUnavailable { get; init; }

    /// <summary>Whether ALL provider results were served from Redis cache.</summary>
    public bool AllFromCache { get; init; }
}
