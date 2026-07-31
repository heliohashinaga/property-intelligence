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

namespace PropertyIntelligence.Providers.Cnes;

/// <summary>
/// CNES/DataSUS health facilities provider — queries PostGIS `health_facilities` table
/// using ST_DWithin and counts facilities by type within specified radii.
/// </summary>
public sealed class CnesHealthProvider : IDataProvider<HealthData>
{
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromDays(36 * 30);

    private readonly PropertyIntelligenceDbContext _dbContext;
    private readonly ILogger<CnesHealthProvider> _logger;
    private readonly ICacheService _cacheService;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "cnes";

    public TimeSpan CacheTtl => _cacheTtl;

    public CnesHealthProvider(
        PropertyIntelligenceDbContext dbContext,
        ILogger<CnesHealthProvider> logger,
        TimeSpan cacheTtl,
        ICacheService cacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl;
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    /// <summary>
    /// Fetches health facility data by executing PostGIS ST_DWithin query against
    /// the health_facilities table. Counts facilities by type within 1km and 2km radii.
    /// </summary>
    public async Task<ProviderFetchResult<HealthData>> FetchAsync(
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
                "CnesHealthProvider: Address missing coordinates. Returning zero counts. Address={NormalizedAddress}",
                address.NormalizedAddress);

            return new ProviderFetchResult<HealthData>
            {
                Data = new HealthData
                {
                    HospitalsWithin2km = 0,
                    ClinicsWith2km = 0,
                    EmergencyUnits2km = 0,
                    InfrastructureTrend = null,
                },
                RawPayload = JsonSerializer.Serialize(new { error = "missing_coordinates" }),
            };
        }

        var lat = address.Lat.Value;
        var lng = address.Lng.Value;

        // Execute PostGIS query: ST_DWithin with geography cast for accurate distance
        var sql = @"
            SELECT 
                facility_type,
                COUNT(*) as count
            FROM health_facilities
            WHERE ST_DWithin(
                location, 
                ST_SetSRID(ST_MakePoint({0}, {1}), 4326)::geography, 
                2000
            )
            GROUP BY facility_type";

        var results = await _dbContext.Database
            .SqlQueryRaw<HealthFacilityQueryResult>(sql, lng, lat)
            .AsNoTracking()
            .ToListAsync(ct);

        var hospitalsCount = results
            .Where(r => r.FacilityType?.ToUpperInvariant().Contains("HOSPITAL") == true)
            .Sum(r => r.Count);

        var clinicsCount = results
            .Where(r => r.FacilityType?.ToUpperInvariant().Contains("UBS") == true 
                     || r.FacilityType?.ToUpperInvariant().Contains("CLINIC") == true)
            .Sum(r => r.Count);

        var emergencyCount = results
            .Where(r => r.FacilityType?.ToUpperInvariant().Contains("UPA") == true
                     || r.FacilityType?.ToUpperInvariant().Contains("SAMU") == true
                     || r.FacilityType?.ToUpperInvariant().Contains("EMERGENCY") == true)
            .Sum(r => r.Count);

        var healthData = new HealthData
        {
            HospitalsWithin2km = hospitalsCount,
            ClinicsWith2km = clinicsCount,
            EmergencyUnits2km = emergencyCount,
            InfrastructureTrend = await ComputeInfrastructureTrendAsync(address, hospitalsCount + clinicsCount + emergencyCount, ct),
        };

        // Serialize result as raw payload for audit trail
        var rawPayload = JsonSerializer.Serialize(new
        {
            query_lat = lat,
            query_lng = lng,
            radius_metres = 2000,
            facilities_by_type = results.Select(r => new { r.FacilityType, r.Count }),
            hospitals = hospitalsCount,
            clinics = clinicsCount,
            emergency_units = emergencyCount,
        });

        _logger.LogInformation(
            "CnesHealthProvider: Fetched health data. Address={NormalizedAddress}, Hospitals={Hospitals}, Clinics={Clinics}, Emergency={Emergency}, Trend={Trend}",
            address.NormalizedAddress,
            hospitalsCount,
            clinicsCount,
            emergencyCount,
            healthData.InfrastructureTrend?.ToString() ?? "unavailable");

        return new ProviderFetchResult<HealthData>
        {
            Data = healthData,
            RawPayload = rawPayload,
        };
    }

    /// <summary>
    /// Compares current total health-facility count against the prior Redis snapshot to
    /// derive a 36-month <see cref="TrendDirection"/> for the infrastructure dimension.
    ///
    /// <para>Thresholds: &gt;10 % increase → Improving; &gt;10 % decrease → Worsening;
    /// otherwise Stable. Returns null when no prior snapshot exists (first fetch).</para>
    /// </summary>
    private async Task<TrendDirection?> ComputeInfrastructureTrendAsync(
        PropertyAddress address,
        int totalFacilities,
        CancellationToken ct)
    {
        var snapshotKey = BuildSnapshotKey(address);
        TrendDirection? trend = null;

        var prior = await _cacheService.GetAsync<FacilitySnapshot>(snapshotKey, ct);
        if (prior is not null && prior.TotalFacilities > 0)
        {
            var ratio = totalFacilities / (double)prior.TotalFacilities;
            trend = ratio switch
            {
                > 1.10 => TrendDirection.Improving,
                < 0.90 => TrendDirection.Worsening,
                _ => TrendDirection.Stable,
            };
        }

        await _cacheService.SetAsync(
            snapshotKey,
            new FacilitySnapshot { TotalFacilities = totalFacilities, RecordedAt = DateTime.UtcNow },
            SnapshotTtl,
            ct);

        return trend;
    }

    private static string BuildSnapshotKey(PropertyAddress address)
    {
        var normalized = address.NormalizedAddress?.ToLowerInvariant() ?? string.Empty;
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        return $"cnes:trend_snapshot:{hash}";
    }

    private sealed class FacilitySnapshot
    {
        public int TotalFacilities { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    /// <summary>
    /// Internal DTO for mapping PostGIS query result.
    /// </summary>
    private sealed class HealthFacilityQueryResult
    {
        public string? FacilityType { get; set; }
        public int Count { get; set; }
    }
}
