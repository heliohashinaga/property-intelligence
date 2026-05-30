using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Shared;

namespace PropertyIntelligence.Providers.Cnes;

/// <summary>
/// Queries the local PostGIS <c>health_facilities</c> table (CNES / DataSUS import).
/// Returns counts of hospitals, clinics, and emergency units within 2 km of the address.
/// </summary>
public sealed class CnesHealthProvider : IDataProvider<HealthData>
{
    private readonly IDbContextFactory<PropertyIntelligenceDbContext> _dbFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<CnesHealthProvider> _logger;

    public string   ProviderName => "cnes";
    public TimeSpan CacheTtl     => TimeSpan.FromDays(7);

    public CnesHealthProvider(
        IDbContextFactory<PropertyIntelligenceDbContext> dbFactory,
        ICacheService cache,
        ILogger<CnesHealthProvider> logger)
    {
        _dbFactory = dbFactory;
        _cache     = cache;
        _logger    = logger;
    }

    public async Task<HealthData> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyHelper.BuildKey(ProviderName, address.NormalizedAddress);

        var cached = await _cache.GetAsync<HealthData>(cacheKey, ct);
        if (cached is not null)
            return cached;

        if (address.Lat is null || address.Lng is null)
        {
            _logger.LogWarning("CnesHealthProvider: no coordinates for {Address}", address.NormalizedAddress);
            return new HealthData();
        }

        HealthData result;
        try
        {
            await using var ctx  = await _dbFactory.CreateDbContextAsync(ct);
            await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
            await conn.OpenAsync(ct);

            // Returns one row per facility_type with count and distance to nearest facility of that type
            const string sql = """
                SELECT facility_type,
                       COUNT(*)::int AS cnt,
                       MIN(ST_Distance(
                           location::geography,
                           ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography
                       )) AS nearest_m
                FROM   health_facilities
                WHERE  ST_DWithin(
                           location::geography,
                           ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
                           2000
                       )
                GROUP  BY facility_type
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("lng", address.Lng.Value);
            cmd.Parameters.AddWithValue("lat", address.Lat.Value);

            int hospitalsWithin2km = 0;
            int clinicsWithin2km   = 0;
            int emergencyUnits2km  = 0;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var type    = reader.GetString(0).Trim().ToUpperInvariant();
                var count   = reader.GetInt32(1);

                if (type is "HOSPITAL")
                    hospitalsWithin2km += count;
                else if (type is "UBS" or "UPA" or "APS" or "CLINICA")
                    clinicsWithin2km += count;
                else if (type is "EMERGENCIA" or "SAMU" or "UPA")
                    emergencyUnits2km += count;
            }

            result = new HealthData
            {
                HospitalsWithin2km = hospitalsWithin2km,
                ClinicsWith2km     = clinicsWithin2km,
                EmergencyUnits2km  = emergencyUnits2km,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CnesHealthProvider: query failed for {Address}", address.NormalizedAddress);
            return new HealthData();
        }

        await _cache.SetAsync(cacheKey, result, CacheTtl, ct);
        return result;
    }
}
