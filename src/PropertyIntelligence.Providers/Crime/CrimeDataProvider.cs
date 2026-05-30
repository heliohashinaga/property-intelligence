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
/// Returns crime rate per 100 k residents and 24-month trend direction.
/// </summary>
public sealed partial class CrimeDataProvider : IDataProvider<CrimeData>
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

        var now        = DateTimeOffset.UtcNow;
        var cutoffYear = now.Year - 2;
        var city       = address.City;

        CrimeData result;
        try
        {
            await using var ctx  = await _dbFactory.CreateDbContextAsync(ct);
            await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
            await conn.OpenAsync(ct);

            // ── 1. Current rate: total crimes over 24-month window ────────────
            const string totalSql = """
                SELECT COALESCE(SUM(count), 0)::bigint AS total_crimes
                FROM   crime_records
                WHERE  municipality = @city
                  AND  year >= @cutoffYear
                """;

            await using var totalCmd = new NpgsqlCommand(totalSql, conn);
            totalCmd.Parameters.AddWithValue("city",       city);
            totalCmd.Parameters.AddWithValue("cutoffYear", cutoffYear);

            var scalar      = await totalCmd.ExecuteScalarAsync(ct);
            var totalCrimes = scalar is DBNull or null ? 0L : Convert.ToInt64(scalar);
            var per100k     = Math.Round(totalCrimes / (FallbackPopulation / 100_000.0), 2);

            // ── 2. Trend: per_100k grouped by month over last 24 months ───────
            // Returns rows: (year, month, total_crimes) ordered oldest → newest
            const string trendSql = """
                SELECT   year, month, SUM(count)::double precision AS monthly_crimes
                FROM     crime_records
                WHERE    municipality = @city
                  AND    (year > @cutoffYear OR (year = @cutoffYear AND month >= @cutoffMonth))
                GROUP BY year, month
                ORDER BY year, month
                """;

            await using var trendCmd = new NpgsqlCommand(trendSql, conn);
            trendCmd.Parameters.AddWithValue("city",        city);
            trendCmd.Parameters.AddWithValue("cutoffYear",  cutoffYear);
            trendCmd.Parameters.AddWithValue("cutoffMonth", now.Month);

            var monthlyRates = new List<double>();
            await using var reader = await trendCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var monthlyCrimes = reader.GetDouble(2);
                // Normalise to per-100k for a comparable series
                monthlyRates.Add(monthlyCrimes / (FallbackPopulation / 100_000.0));
            }

            var securityTrend = TrendCalculator.LinearSlope(monthlyRates);

            result = new CrimeData
            {
                CrimeRatePer100k = per100k,
                YoyChangePct     = null,   // retained for backwards compat
                SecurityTrend    = securityTrend,
            };
        }
        catch (Exception ex)
        {
            LogQueryFailed(_logger, ex, city);
            return new CrimeData();
        }

        await _cache.SetAsync(cacheKey, result, CacheTtl, ct);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "CrimeDataProvider: query failed for {City}")]
    private static partial void LogQueryFailed(ILogger logger, Exception ex, string city);
}
