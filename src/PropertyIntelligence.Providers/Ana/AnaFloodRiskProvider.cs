using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Ana;

/// <summary>
/// ANA SNIRH flood-risk provider — queries PostGIS `flood_risk_zones` table
/// and returns the highest risk level intersecting the address point.
/// </summary>
public sealed class AnaFloodRiskProvider : IDataProvider<FloodRiskData>
{
    private readonly PropertyIntelligenceDbContext _dbContext;
    private readonly ILogger<AnaFloodRiskProvider> _logger;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "ana_flood_risk";

    public TimeSpan CacheTtl => _cacheTtl;

    public AnaFloodRiskProvider(
        PropertyIntelligenceDbContext dbContext,
        ILogger<AnaFloodRiskProvider> logger,
        TimeSpan cacheTtl)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl;
    }

    /// <summary>
    /// Fetches flood risk data by executing PostGIS ST_Intersects query against
    /// the flood_risk_zones table. Returns the highest risk level found, or null
    /// if no flood zone intersects the address point.
    /// </summary>
    public async Task<ProviderFetchResult<FloodRiskData>> FetchAsync(
        PropertyAddress address,
        CancellationToken ct = default)
    {
        if (address is null)
        {
            throw new ArgumentNullException(nameof(address));
        }

        // Require coordinates to perform spatial query
        if (!address.Lat.HasValue || !address.Lng.HasValue)
        {
            _logger.LogWarning(
                "AnaFloodRiskProvider: Address missing coordinates. Returning null risk level. Address={NormalizedAddress}",
                address.NormalizedAddress);

            return new ProviderFetchResult<FloodRiskData>
            {
                Data = new FloodRiskData
                {
                    RiskLevel = null,
                    DistanceMetres = null,
                    Trend = TrendDirection.Stable,
                },
                RawPayload = JsonSerializer.Serialize(new { error = "missing_coordinates" }),
            };
        }

        var lat = address.Lat.Value;
        var lng = address.Lng.Value;

        // Execute PostGIS query: ST_Intersects with address point
        // Query raw SQL since flood_risk_zones is not mapped as an entity
        var sql = @"
            SELECT risk_level, 
                   ST_Distance(geometry::geography, ST_SetSRID(ST_MakePoint({0}, {1}), 4326)::geography) as distance_metres
            FROM flood_risk_zones
            WHERE ST_Intersects(geometry, ST_SetSRID(ST_MakePoint({0}, {1}), 4326))
            ORDER BY 
                CASE risk_level
                    WHEN 'critical' THEN 1
                    WHEN 'high' THEN 2
                    WHEN 'moderate' THEN 3
                    WHEN 'low' THEN 4
                    ELSE 5
                END
            LIMIT 1";

        var result = await _dbContext.Database
            .SqlQueryRaw<FloodZoneQueryResult>(sql, lng, lat)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        var floodRiskData = new FloodRiskData
        {
            RiskLevel = result?.RiskLevel,
            DistanceMetres = result?.DistanceMetres ?? 0.0,
            Trend = TrendDirection.Stable, // Always stable at MVP (per T044)
        };

        // Serialize result as raw payload for audit trail
        var rawPayload = JsonSerializer.Serialize(new
        {
            query_lat = lat,
            query_lng = lng,
            risk_level = result?.RiskLevel,
            distance_metres = result?.DistanceMetres,
            intersects = result != null,
        });

        _logger.LogInformation(
            "AnaFloodRiskProvider: Fetched flood risk. Address={NormalizedAddress}, RiskLevel={RiskLevel}, Distance={DistanceMetres}m",
            address.NormalizedAddress,
            floodRiskData.RiskLevel ?? "none",
            floodRiskData.DistanceMetres);

        return new ProviderFetchResult<FloodRiskData>
        {
            Data = floodRiskData,
            RawPayload = rawPayload,
        };
    }

    /// <summary>
    /// Internal DTO for mapping PostGIS query result.
    /// </summary>
    private sealed class FloodZoneQueryResult
    {
        public string RiskLevel { get; set; } = string.Empty;
        public double DistanceMetres { get; set; }
    }
}
