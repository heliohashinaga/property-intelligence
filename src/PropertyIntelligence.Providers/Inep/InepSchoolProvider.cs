using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Shared;

namespace PropertyIntelligence.Providers.Inep;

/// <summary>
/// Queries the local PostGIS <c>school_records</c> table (INEP / IDEB import).
/// Returns the nearest school's IDEB score and counts of high-quality schools within 2 km.
/// </summary>
public sealed class InepSchoolProvider : IDataProvider<SchoolData>
{
    private readonly IDbContextFactory<PropertyIntelligenceDbContext> _dbFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<InepSchoolProvider> _logger;

    public string   ProviderName => "inep";
    public TimeSpan CacheTtl     => TimeSpan.FromDays(30);

    public InepSchoolProvider(
        IDbContextFactory<PropertyIntelligenceDbContext> dbFactory,
        ICacheService cache,
        ILogger<InepSchoolProvider> logger)
    {
        _dbFactory = dbFactory;
        _cache     = cache;
        _logger    = logger;
    }

    public async Task<SchoolData> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyHelper.BuildKey(ProviderName, address.NormalizedAddress);

        var cached = await _cache.GetAsync<SchoolData>(cacheKey, ct);
        if (cached is not null)
            return cached;

        if (address.Lat is null || address.Lng is null)
        {
            _logger.LogWarning("InepSchoolProvider: no coordinates for {Address}", address.NormalizedAddress);
            return new SchoolData();
        }

        SchoolData result;
        try
        {
            await using var ctx  = await _dbFactory.CreateDbContextAsync(ct);
            await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
            await conn.OpenAsync(ct);

            // Returns all schools within 2 km, ordered by distance
            const string sql = """
                SELECT ideb_score,
                       ST_Distance(
                           location::geography,
                           ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography
                       ) AS dist_m
                FROM   school_records
                WHERE  ST_DWithin(
                           location::geography,
                           ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
                           2000
                       )
                ORDER  BY dist_m
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("lng", address.Lng.Value);
            cmd.Parameters.AddWithValue("lat", address.Lat.Value);

            double? nearestIdeb         = null;
            int     totalWithin2km      = 0;
            int     highQuality1km      = 0;
            int     highQuality2km      = 0;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var ideb   = reader.IsDBNull(0) ? (double?)null : reader.GetDouble(0);
                var distM  = reader.GetDouble(1);

                if (totalWithin2km == 0)
                    nearestIdeb = ideb;   // first row = nearest

                totalWithin2km++;

                if (ideb is >= 7.0)
                {
                    if (distM <= 1000.0) highQuality1km++;
                    highQuality2km++;
                }
            }

            result = new SchoolData
            {
                SchoolsWithin2km   = totalWithin2km,
                NearestSchoolIdeb  = nearestIdeb,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InepSchoolProvider: query failed for {Address}", address.NormalizedAddress);
            return new SchoolData();
        }

        await _cache.SetAsync(cacheKey, result, CacheTtl, ct);
        return result;
    }
}
