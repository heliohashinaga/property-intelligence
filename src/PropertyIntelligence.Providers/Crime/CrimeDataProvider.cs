using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Core.Services;

namespace PropertyIntelligence.Providers.Crime;

/// <summary>
/// SSP-SP crime data provider — queries `crime_records` table aggregating
/// crime incidents by municipality and calculating per 100k rate.
/// </summary>
public sealed class CrimeDataProvider : IDataProvider<CrimeData>
{
    private readonly PropertyIntelligenceDbContext _dbContext;
    private readonly ILogger<CrimeDataProvider> _logger;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "ssp_sp";

    public TimeSpan CacheTtl => _cacheTtl;

    public CrimeDataProvider(
        PropertyIntelligenceDbContext dbContext,
        ILogger<CrimeDataProvider> logger,
        TimeSpan cacheTtl)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl;
    }

    /// <summary>
    /// Fetches crime data by querying crime_records for the municipality,
    /// summing incidents over the last 12 months, calculating rate per 100k,
    /// and computing a 24-month linear regression trend (T042).
    /// </summary>
    public async Task<ProviderFetchResult<CrimeData>> FetchAsync(
        PropertyAddress address,
        CancellationToken ct = default)
    {
        if (address is null)
        {
            throw new ArgumentNullException(nameof(address));
        }

        var municipality = address.City;
        var now = DateTime.UtcNow;

        // ── 12-month aggregate for current rate ──────────────────────────────
        var cutoffYear = now.AddMonths(-12).Year;
        var aggregateSql = @"
            SELECT 
                SUM(count) as total_incidents,
                MAX(population) as population
            FROM crime_records
            WHERE municipality = {0} AND year >= {1}";

        var aggregate = await _dbContext.Database
            .SqlQueryRaw<CrimeAggregateResult>(aggregateSql, municipality, cutoffYear)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        double? crimeRatePer100k = null;
        if (aggregate?.TotalIncidents > 0 && aggregate.Population > 0)
        {
            crimeRatePer100k = (aggregate.TotalIncidents / (double)aggregate.Population) * 100000;
        }

        // ── 24-month monthly series for linear regression trend (T042) ───────
        // Cut off = 24 months ago expressed as (year * 12 + month) integer
        var cutoffMonthIndex = now.Year * 12 + now.Month - 24;
        var trendSql = @"
            SELECT
                (year * 12 + month) AS year_month_index,
                SUM(count)::float / NULLIF(MAX(population), 0) * 100000 AS per_100k
            FROM crime_records
            WHERE municipality = {0}
              AND (year * 12 + month) >= {1}
            GROUP BY year, month
            ORDER BY year, month";

        var monthlySeries = await _dbContext.Database
            .SqlQueryRaw<CrimeMonthlyPoint>(trendSql, municipality, cutoffMonthIndex)
            .AsNoTracking()
            .ToListAsync(ct);

        TrendDirection? securityTrend = null;
        double? yoyChangePct = null;

        if (monthlySeries.Count >= 2)
        {
            // Map to chronological (monthIndex, per100k) pairs starting at 0
            var baseIndex = monthlySeries[0].YearMonthIndex;
            var dataPoints = monthlySeries
                .Select(p => (MonthIndex: p.YearMonthIndex - baseIndex, Per100k: p.Per100k))
                .ToList();

            securityTrend = TrendCalculator.ComputeTrend(dataPoints);

            // Year-over-year: compare last 12 months average vs prior 12 months average
            var recentAvg = monthlySeries.Skip(monthlySeries.Count - 12).Average(p => p.Per100k);
            var priorAvg = monthlySeries.Take(12).Average(p => p.Per100k);
            if (priorAvg > 0)
            {
                yoyChangePct = (recentAvg - priorAvg) / priorAvg * 100.0;
            }
        }

        var crimeData = new CrimeData
        {
            CrimeRatePer100k = crimeRatePer100k,
            YoyChangePct = yoyChangePct,
            SecurityTrend = securityTrend,
        };

        var rawPayload = JsonSerializer.Serialize(new
        {
            municipality,
            cutoff_year = cutoffYear,
            total_incidents = aggregate?.TotalIncidents,
            population = aggregate?.Population,
            crime_rate_per_100k = crimeRatePer100k,
            data_available = aggregate != null,
            monthly_series_count = monthlySeries.Count,
            security_trend = securityTrend?.ToString(),
            yoy_change_pct = yoyChangePct,
        });

        _logger.LogInformation(
            "CrimeDataProvider: Fetched crime data. Municipality={Municipality}, RatePer100k={Rate}, Trend={Trend}",
            municipality,
            crimeRatePer100k?.ToString("F2") ?? "none",
            securityTrend?.ToString() ?? "unavailable");

        return new ProviderFetchResult<CrimeData>
        {
            Data = crimeData,
            RawPayload = rawPayload,
        };
    }

    /// <summary>Internal DTO for 12-month aggregate query.</summary>
    private sealed class CrimeAggregateResult
    {
        public long? TotalIncidents { get; set; }
        public long? Population { get; set; }
    }

    /// <summary>Internal DTO for 24-month monthly series query.</summary>
    private sealed class CrimeMonthlyPoint
    {
        public int YearMonthIndex { get; set; }
        public double Per100k { get; set; }
    }
}
