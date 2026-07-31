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

namespace PropertyIntelligence.Providers.Inep;

/// <summary>
/// INEP/IDEB school data provider — queries PostGIS `school_records` table
/// using ST_DWithin and returns nearest school IDEB scores and counts within radii.
/// </summary>
public sealed class InepSchoolProvider : IDataProvider<SchoolData>
{
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromDays(36 * 30);

    private readonly PropertyIntelligenceDbContext _dbContext;
    private readonly ILogger<InepSchoolProvider> _logger;
    private readonly ICacheService _cacheService;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "inep";

    public TimeSpan CacheTtl => _cacheTtl;

    public InepSchoolProvider(
        PropertyIntelligenceDbContext dbContext,
        ILogger<InepSchoolProvider> logger,
        TimeSpan cacheTtl,
        ICacheService cacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl;
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    /// <summary>
    /// Fetches school data by executing PostGIS ST_DWithin query against
    /// the school_records table. Returns nearest school IDEB and counts excellence schools.
    /// </summary>
    public async Task<ProviderFetchResult<SchoolData>> FetchAsync(
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
                "InepSchoolProvider: Address missing coordinates. Returning zero counts. Address={NormalizedAddress}",
                address.NormalizedAddress);

            return new ProviderFetchResult<SchoolData>
            {
                Data = new SchoolData
                {
                    SchoolsWithin2km = 0,
                    NearestSchoolIdeb = null,
                    InfrastructureTrend = null,
                },
                RawPayload = JsonSerializer.Serialize(new { error = "missing_coordinates" }),
            };
        }

        var lat = address.Lat.Value;
        var lng = address.Lng.Value;

        // Count schools within 2km
        var countSql = @"
            SELECT COUNT(*) as count
            FROM school_records
            WHERE ST_DWithin(
                location, 
                ST_SetSRID(ST_MakePoint({0}, {1}), 4326)::geography, 
                2000
            )";

        var countResult = await _dbContext.Database
            .SqlQueryRaw<CountQueryResult>(countSql, lng, lat)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        // Find nearest school with IDEB
        var nearestSql = @"
            SELECT 
                ideb_score,
                ST_Distance(location::geography, ST_SetSRID(ST_MakePoint({0}, {1}), 4326)::geography) as distance_metres
            FROM school_records
            WHERE ideb_score IS NOT NULL
            ORDER BY location <-> ST_SetSRID(ST_MakePoint({0}, {1}), 4326)
            LIMIT 1";

        var nearestResult = await _dbContext.Database
            .SqlQueryRaw<SchoolQueryResult>(nearestSql, lng, lat)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        var schoolData = new SchoolData
        {
            SchoolsWithin2km = countResult?.Count ?? 0,
            NearestSchoolIdeb = nearestResult?.IdebScore,
            InfrastructureTrend = await ComputeInfrastructureTrendAsync(address, countResult?.Count ?? 0, ct),
        };

        // Serialize result as raw payload for audit trail
        var rawPayload = JsonSerializer.Serialize(new
        {
            query_lat = lat,
            query_lng = lng,
            radius_metres = 2000,
            schools_within_2km = schoolData.SchoolsWithin2km,
            nearest_school_ideb = nearestResult?.IdebScore,
            nearest_school_distance_metres = nearestResult?.DistanceMetres,
        });

        _logger.LogInformation(
            "InepSchoolProvider: Fetched school data. Address={NormalizedAddress}, SchoolsCount={Count}, NearestIDEB={IDEB}, Trend={Trend}",
            address.NormalizedAddress,
            schoolData.SchoolsWithin2km,
            schoolData.NearestSchoolIdeb?.ToString("F1") ?? "none",
            schoolData.InfrastructureTrend?.ToString() ?? "unavailable");

        return new ProviderFetchResult<SchoolData>
        {
            Data = schoolData,
            RawPayload = rawPayload,
        };
    }

    /// <summary>
    /// Compares current school count against the prior Redis snapshot to
    /// derive a 36-month <see cref="TrendDirection"/> for the infrastructure dimension.
    ///
    /// <para>Thresholds: &gt;10 % increase → Improving; &gt;10 % decrease → Worsening;
    /// otherwise Stable. Returns null when no prior snapshot exists (first fetch).</para>
    /// </summary>
    private async Task<TrendDirection?> ComputeInfrastructureTrendAsync(
        PropertyAddress address,
        int schoolCount,
        CancellationToken ct)
    {
        var snapshotKey = BuildSnapshotKey(address);
        TrendDirection? trend = null;

        var prior = await _cacheService.GetAsync<SchoolSnapshot>(snapshotKey, ct);
        if (prior is not null && prior.SchoolCount > 0)
        {
            var ratio = schoolCount / (double)prior.SchoolCount;
            trend = ratio switch
            {
                > 1.10 => TrendDirection.Improving,
                < 0.90 => TrendDirection.Worsening,
                _ => TrendDirection.Stable,
            };
        }

        await _cacheService.SetAsync(
            snapshotKey,
            new SchoolSnapshot { SchoolCount = schoolCount, RecordedAt = DateTime.UtcNow },
            SnapshotTtl,
            ct);

        return trend;
    }

    private static string BuildSnapshotKey(PropertyAddress address)
    {
        var normalized = address.NormalizedAddress?.ToLowerInvariant() ?? string.Empty;
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        return $"inep:trend_snapshot:{hash}";
    }

    private sealed class SchoolSnapshot
    {
        public int SchoolCount { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    /// <summary>
    /// Internal DTO for mapping PostGIS query result.
    /// </summary>
    private sealed class SchoolQueryResult
    {
        public double? IdebScore { get; set; }
        public double? DistanceMetres { get; set; }
    }

    private sealed class CountQueryResult
    {
        public int Count { get; set; }
    }
}
