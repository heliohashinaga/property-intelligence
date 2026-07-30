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
    private readonly PropertyIntelligenceDbContext _dbContext;
    private readonly ILogger<IbgeCensusProvider> _logger;
    private readonly TimeSpan _cacheTtl;

    public string ProviderName => "ibge_census";

    public TimeSpan CacheTtl => _cacheTtl;

    public IbgeCensusProvider(
        PropertyIntelligenceDbContext dbContext,
        ILogger<IbgeCensusProvider> logger,
        TimeSpan cacheTtl)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl;
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
            UrbanContextTrend = null, // Populated in US2 (Phase 4)
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
            "IbgeCensusProvider: Fetched census data. Address={NormalizedAddress}, IncomeGroup={IncomeGroup}, Density={Density}",
            address.NormalizedAddress,
            censusData.MedianIncomeGroup?.ToString() ?? "none",
            censusData.PopulationDensity?.ToString("F2") ?? "none");

        return new ProviderFetchResult<CensusData>
        {
            Data = censusData,
            RawPayload = rawPayload,
        };
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
