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
    /// summing incidents over the last 12 months and calculating rate per 100k.
    /// </summary>
    public async Task<ProviderFetchResult<CrimeData>> FetchAsync(
        PropertyAddress address,
        CancellationToken ct = default)
    {
        if (address is null)
        {
            throw new ArgumentNullException(nameof(address));
        }

        // Calculate cutoff year (last 12 months from now)
        var cutoffYear = DateTime.UtcNow.AddMonths(-12).Year;
        var municipality = address.City;

        // Query crime records for the municipality
        var sql = @"
            SELECT 
                SUM(count) as total_incidents,
                MAX(population) as population
            FROM crime_records
            WHERE municipality = {0} AND year >= {1}";

        var result = await _dbContext.Database
            .SqlQueryRaw<CrimeQueryResult>(sql, municipality, cutoffYear)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        double? crimeRatePer100k = null;
        if (result?.TotalIncidents > 0 && result.Population > 0)
        {
            crimeRatePer100k = (result.TotalIncidents / (double)result.Population) * 100000;
        }

        var crimeData = new CrimeData
        {
            CrimeRatePer100k = crimeRatePer100k,
            YoyChangePct = null, // Populated in US2 with trend calculation
            SecurityTrend = null, // Populated in US2 (Phase 4)
        };

        // Serialize result as raw payload for audit trail
        var rawPayload = JsonSerializer.Serialize(new
        {
            municipality,
            cutoff_year = cutoffYear,
            total_incidents = result?.TotalIncidents,
            population = result?.Population,
            crime_rate_per_100k = crimeRatePer100k,
            data_available = result != null,
        });

        _logger.LogInformation(
            "CrimeDataProvider: Fetched crime data. Municipality={Municipality}, RatePer100k={Rate}",
            municipality,
            crimeRatePer100k?.ToString("F2") ?? "none");

        return new ProviderFetchResult<CrimeData>
        {
            Data = crimeData,
            RawPayload = rawPayload,
        };
    }

    /// <summary>
    /// Internal DTO for mapping SQL query result.
    /// </summary>
    private sealed class CrimeQueryResult
    {
        public long? TotalIncidents { get; set; }
        public long? Population { get; set; }
    }
}
