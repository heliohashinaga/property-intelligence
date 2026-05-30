using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Shared;

namespace PropertyIntelligence.Providers.Ana;

/// <summary>
/// Queries the local PostGIS <c>flood_risk_zones</c> table imported from ANA SNIRH shapefiles.
/// Returns the highest-severity flood risk level that intersects the address point.
/// </summary>
public sealed partial class AnaFloodRiskProvider : IDataProvider<FloodRiskData>
{
    private readonly IDbContextFactory<PropertyIntelligenceDbContext> _dbFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<AnaFloodRiskProvider> _logger;

    public string   ProviderName => "ana_snirh";
    public TimeSpan CacheTtl     => TimeSpan.FromDays(30);

    public AnaFloodRiskProvider(
        IDbContextFactory<PropertyIntelligenceDbContext> dbFactory,
        ICacheService cache,
        ILogger<AnaFloodRiskProvider> logger)
    {
        _dbFactory = dbFactory;
        _cache     = cache;
        _logger    = logger;
    }

    public async Task<FloodRiskData> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyHelper.BuildKey(ProviderName, address.NormalizedAddress);

        var cached = await _cache.GetAsync<FloodRiskData>(cacheKey, ct);
        if (cached is not null)
            return cached;

        // No coordinates → cannot intersect
        if (address.Lat is null || address.Lng is null)
        {
            LogNoCoordinates(_logger, address.NormalizedAddress);
            return new FloodRiskData { RiskLevel = null };
        }

        FloodRiskData result;
        try
        {
            await using var ctx  = await _dbFactory.CreateDbContextAsync(ct);
            await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT risk_level
                FROM   flood_risk_zones
                WHERE  ST_Intersects(geometry, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326))
                ORDER  BY CASE risk_level
                              WHEN 'critical' THEN 1
                              WHEN 'high'     THEN 2
                              WHEN 'moderate' THEN 3
                              WHEN 'low'      THEN 4
                              ELSE 5
                          END
                LIMIT  1
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("lng", address.Lng.Value);
            cmd.Parameters.AddWithValue("lat", address.Lat.Value);

            var scalar = await cmd.ExecuteScalarAsync(ct);
            var riskLevel = scalar is DBNull or null ? null : (string?)scalar;

            result = new FloodRiskData { RiskLevel = riskLevel };
        }
        catch (Exception ex)
        {
            LogQueryFailed(_logger, ex, address.NormalizedAddress);
            return new FloodRiskData { RiskLevel = null };
        }

        await _cache.SetAsync(cacheKey, result, CacheTtl, ct);
        return result;
    }


    [LoggerMessage(Level = LogLevel.Warning,
        Message = "AnaFloodRiskProvider: no coordinates for {Address}; skipping PostGIS query")]
    private static partial void LogNoCoordinates(ILogger logger, string address);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "AnaFloodRiskProvider: PostGIS query failed for {Address}")]
    private static partial void LogQueryFailed(ILogger logger, Exception ex, string address);
}
