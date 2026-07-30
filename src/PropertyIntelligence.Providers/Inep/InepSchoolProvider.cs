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
    private readonly PropertyIntelligenceDbContext _dbContext;
    private readonly ILogger<InepSchoolProvider> _logger;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "inep";

    public TimeSpan CacheTtl => _cacheTtl;

    public InepSchoolProvider(
        PropertyIntelligenceDbContext dbContext,
        ILogger<InepSchoolProvider> logger,
        TimeSpan cacheTtl)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl;
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
            InfrastructureTrend = null, // Populated in US2 (Phase 4)
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
            "InepSchoolProvider: Fetched school data. Address={NormalizedAddress}, SchoolsCount={Count}, NearestIDEB={IDEB}",
            address.NormalizedAddress,
            schoolData.SchoolsWithin2km,
            schoolData.NearestSchoolIdeb?.ToString("F1") ?? "none");

        return new ProviderFetchResult<SchoolData>
        {
            Data = schoolData,
            RawPayload = rawPayload,
        };
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
