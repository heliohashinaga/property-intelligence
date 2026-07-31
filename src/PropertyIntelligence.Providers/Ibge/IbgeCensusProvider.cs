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

namespace PropertyIntelligence.Providers.Ibge;

/// <summary>
/// IBGE Censo 2022 provider — queries PostGIS `census_sectors` table
/// and returns socioeconomic data for the census sector intersecting the address point.
/// </summary>
public sealed class IbgeCensusProvider : IDataProvider<CensusData>
{
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromDays(36 * 30);

    private readonly PropertyIntelligenceDbContext _dbContext;
    private readonly ILogger<IbgeCensusProvider> _logger;
    private readonly ICacheService _cacheService;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "ibge_census";

    public TimeSpan CacheTtl => _cacheTtl;

    public IbgeCensusProvider(
        PropertyIntelligenceDbContext dbContext,
        ILogger<IbgeCensusProvider> logger,
        TimeSpan cacheTtl,
        ICacheService cacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl;
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    /// <summary>
    /// Fetches census data by executing PostGIS ST_Intersects query against
    /// the census_sectors table. Returns socioeconomic indicators for the
    /// census sector containing the address point.
    /// </summary>
    public async Task<ProviderFetchResult<CensusData>> FetchAsync(
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
                "IbgeCensusProvider: Address missing coordinates. Returning null census data. Address={NormalizedAddress}",
                address.NormalizedAddress);

            return new ProviderFetchResult<CensusData>
            {
                Data = new CensusData
                {
                    MedianIncomeGroup = null,
                    PopulationDensity = null,
                    WorkingAgePct = null,
                    UrbanContextTrend = null,
                },
                RawPayload = JsonSerializer.Serialize(new { error = "missing_coordinates" }),
            };
        }

        var lat = address.Lat.Value;
        var lng = address.Lng.Value;

        // Execute PostGIS query: ST_Intersects with address point
        var sql = @"
            SELECT 
                median_income_group,
                population_density,
                working_age_pct
            FROM census_sectors
            WHERE ST_Intersects(geometry, ST_SetSRID(ST_MakePoint({0}, {1}), 4326))
            LIMIT 1";

        var result = await _dbContext.Database
            .SqlQueryRaw<CensusSectorQueryResult>(sql, lng, lat)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        var censusData = new CensusData
        {
            MedianIncomeGroup = result?.MedianIncomeGroup,
            PopulationDensity = result?.PopulationDensity,
            WorkingAgePct = result?.WorkingAgePct,
            UrbanContextTrend = await ComputeUrbanContextTrendAsync(address, result?.MedianIncomeGroup, ct),
        };

        // Serialize result as raw payload for audit trail
        var rawPayload = JsonSerializer.Serialize(new
        {
            query_lat = lat,
            query_lng = lng,
            median_income_group = result?.MedianIncomeGroup,
            population_density = result?.PopulationDensity,
            working_age_pct = result?.WorkingAgePct,
            sector_found = result != null,
        });

        _logger.LogInformation(
            "IbgeCensusProvider: Fetched census data. Address={NormalizedAddress}, IncomeGroup={IncomeGroup}, Density={Density}, Trend={Trend}",
            address.NormalizedAddress,
            censusData.MedianIncomeGroup?.ToString() ?? "none",
            censusData.PopulationDensity?.ToString("F2") ?? "none",
            censusData.UrbanContextTrend?.ToString() ?? "unavailable");

        return new ProviderFetchResult<CensusData>
        {
            Data = censusData,
            RawPayload = rawPayload,
        };
    }

    /// <summary>
    /// Compares current <c>median_income_group</c> against the prior Redis snapshot to
    /// derive a 36-month <see cref="TrendDirection"/> for the urban context dimension.
    ///
    /// <para>An increase of ≥1 group → Improving; a decrease of ≥1 group → Worsening;
    /// same → Stable. Returns null when <paramref name="currentIncomeGroup"/> is null
    /// or no prior snapshot exists (first fetch).</para>
    /// </summary>
    private async Task<TrendDirection?> ComputeUrbanContextTrendAsync(
        PropertyAddress address,
        int? currentIncomeGroup,
        CancellationToken ct)
    {
        if (currentIncomeGroup is null)
        {
            return null;
        }

        var snapshotKey = BuildSnapshotKey(address);
        TrendDirection? trend = null;

        var prior = await _cacheService.GetAsync<CensusSnapshot>(snapshotKey, ct);
        if (prior?.MedianIncomeGroup is not null)
        {
            var delta = currentIncomeGroup.Value - prior.MedianIncomeGroup.Value;
            trend = delta switch
            {
                >= 1 => TrendDirection.Improving,
                <= -1 => TrendDirection.Worsening,
                _ => TrendDirection.Stable,
            };
        }

        await _cacheService.SetAsync(
            snapshotKey,
            new CensusSnapshot { MedianIncomeGroup = currentIncomeGroup, RecordedAt = DateTime.UtcNow },
            SnapshotTtl,
            ct);

        return trend;
    }

    private static string BuildSnapshotKey(PropertyAddress address)
    {
        var normalized = address.NormalizedAddress?.ToLowerInvariant() ?? string.Empty;
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        return $"ibge_census:trend_snapshot:{hash}";
    }

    private sealed class CensusSnapshot
    {
        public int? MedianIncomeGroup { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    /// <summary>
    /// Internal DTO for mapping PostGIS query result.
    /// </summary>
    private sealed class CensusSectorQueryResult
    {
        public int? MedianIncomeGroup { get; set; }
        public double? PopulationDensity { get; set; }
        public double? WorkingAgePct { get; set; }
    }
}
