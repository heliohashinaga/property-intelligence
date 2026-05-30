using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Shared;

namespace PropertyIntelligence.Providers.Crime;

/// <summary>
/// Queries the local <c>crime_records</c> table (SSP-SP CSV import).
/// Returns crime rate per 100 k residents for a rolling 2-year window.
/// </summary>
public sealed class CrimeDataProvider : IDataProvider<CrimeData>
{
    // Fallback population when no IBGE census data is available.
    // São Paulo city population (IBGE 2022 estimate).
    private const double FallbackPopulation = 11_451_245.0;

    private readonly IDbContextFactory<PropertyIntelligenceDbContext> _dbFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<CrimeDataProvider> _logger;

    public string   ProviderName => "ssp_sp";
    public TimeSpan CacheTtl     => TimeSpan.FromHours(24);

    public CrimeDataProvider(
        IDbContextFactory<PropertyIntelligenceDbContext> dbFactory,
        ICacheService cache,
        ILogger<CrimeDataProvider> logger)
    {
        _dbFactory = dbFactory;
        _cache     = cache;
        _logger    = logger;
    }

    public async Task<CrimeData> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyHelper.BuildKey(ProviderName, address.NormalizedAddress);

        var cached = await _cache.GetAsync<CrimeData>(cacheKey, ct);
        if (cached is not null)
            return cached;

        var cutoffYear = DateTimeOffset.UtcNow.Year - 2;
        var city       = address.City;

        CrimeData result;
        try
        {
            await using var ctx  = await _dbFactory.CreateDbContextAsync(ct);
            await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
            await conn.OpenAsync(ct);

            // Aggregate total crimes across all types for the rolling window
            const string sql = """
                SELECT COALESCE(SUM(count), 0)::bigint AS total_crimes
                FROM   crime_records
                WHERE  municipality = @city
                  AND  year >= @cutoffYear
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("city",       city);
            cmd.Parameters.AddWithValue("cutoffYear", cutoffYear);

            var scalar      = await cmd.ExecuteScalarAsync(ct);
            var totalCrimes = scalar is DBNull or null ? 0L : Convert.ToInt64(scalar);

            // Crime rate per 100 k using fallback population
            var per100k = totalCrimes / (FallbackPopulation / 100_000.0);

            result = new CrimeData
            {
                CrimeRatePer100k = Math.Round(per100k, 2),
                YoyChangePct     = null,  // Populated in Phase 4 (US2)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CrimeDataProvider: query failed for {City}", city);
            return new CrimeData();
        }

        await _cache.SetAsync(cacheKey, result, CacheTtl, ct);
        return result;
    }
}
