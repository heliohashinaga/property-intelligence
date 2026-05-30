using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Shared;

namespace PropertyIntelligence.Providers.Ibge;

/// <summary>
/// Queries the local PostGIS <c>census_sectors</c> table (IBGE Censo 2022).
/// Returns socioeconomic data for the census sector that contains the address point.
/// </summary>
public sealed class IbgeCensusProvider : IDataProvider<CensusData>
{
    private readonly IDbContextFactory<PropertyIntelligenceDbContext> _dbFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<IbgeCensusProvider> _logger;

    public string   ProviderName => "ibge_census";
    public TimeSpan CacheTtl     => TimeSpan.FromDays(30);

    public IbgeCensusProvider(
        IDbContextFactory<PropertyIntelligenceDbContext> dbFactory,
        ICacheService cache,
        ILogger<IbgeCensusProvider> logger)
    {
        _dbFactory = dbFactory;
        _cache     = cache;
        _logger    = logger;
    }

    public async Task<CensusData> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyHelper.BuildKey(ProviderName, address.NormalizedAddress);

        var cached = await _cache.GetAsync<CensusData>(cacheKey, ct);
        if (cached is not null)
            return cached;

        if (address.Lat is null || address.Lng is null)
        {
            _logger.LogWarning("IbgeCensusProvider: no coordinates for {Address}", address.NormalizedAddress);
            return new CensusData();
        }

        CensusData result;
        try
        {
            await using var ctx  = await _dbFactory.CreateDbContextAsync(ct);
            await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT median_income_group,
                       population_density,
                       median_age
                FROM   census_sectors
                WHERE  ST_Intersects(geometry, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326))
                LIMIT  1
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("lng", address.Lng.Value);
            cmd.Parameters.AddWithValue("lat", address.Lat.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                result = new CensusData
                {
                    MedianIncomeGroup = reader.IsDBNull(0) ? null : (int?)reader.GetInt32(0),
                    PopulationDensity = reader.IsDBNull(1) ? null : (double?)reader.GetDouble(1),
                    // median_age mapped to WorkingAgePct as a proxy (Phase 4 will add proper working-age % column)
                    WorkingAgePct     = reader.IsDBNull(2) ? null : (double?)reader.GetDouble(2),
                };
            }
            else
            {
                result = new CensusData();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IbgeCensusProvider: query failed for {Address}", address.NormalizedAddress);
            return new CensusData();
        }

        await _cache.SetAsync(cacheKey, result, CacheTtl, ct);
        return result;
    }
}
