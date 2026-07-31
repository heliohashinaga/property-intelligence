using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Transit;

/// <summary>
/// Official São Paulo mobility provider backed by SPTrans stop data and
/// GeoSampa/Metrô/CPTM station layers.
/// </summary>
public sealed class SpTransGeoSampaTransitProvider : IDataProvider<PoiData>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromDays(36 * 30);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheTtl;
    private readonly ICacheService _cacheService;
    private readonly ILogger<SpTransGeoSampaTransitProvider> _logger;

    public SpTransGeoSampaTransitProvider(
        HttpClient httpClient,
        TimeSpan cacheTtl,
        ICacheService cacheService,
        ILogger<SpTransGeoSampaTransitProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheTtl = cacheTtl;
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => "sptrans_geosampa";

    public TimeSpan CacheTtl => _cacheTtl;

    public async Task<ProviderFetchResult<PoiData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (!address.Lat.HasValue || !address.Lng.HasValue)
        {
            throw new InvalidOperationException("Official São Paulo transit queries require address coordinates.");
        }

        var spTransPath = BuildRequestPath("sptrans/stops", address, 1000);
        var geoSampaPath = BuildRequestPath("geosampa/stations", address, 1000);

        var spTransBodyTask = GetBodyAsync(spTransPath, ct);
        var geoSampaBodyTask = GetBodyAsync(geoSampaPath, ct);

        await Task.WhenAll(spTransBodyTask, geoSampaBodyTask);

        var spTransBody = await spTransBodyTask;
        var geoSampaBody = await geoSampaBodyTask;

        var spTransResponse = Deserialize<SpTransStopsResponse>(spTransBody, "SPTrans");
        var geoSampaResponse = Deserialize<GeoSampaStationsResponse>(geoSampaBody, "GeoSampa");

        var allDistances = spTransResponse.Stops
            .Where(static stop => stop.DistanceMetres.HasValue)
            .Select(static stop => new TransitObservation("sptrans", NormalizeId(stop.Id, stop.Name), stop.DistanceMetres!.Value))
            .Concat(
                geoSampaResponse.Stations
                    .Where(static station => station.DistanceMetres.HasValue)
                    .Select(static station => new TransitObservation("geosampa", NormalizeId(station.Id, station.Name), station.DistanceMetres!.Value)))
            .GroupBy(static item => item.UniqueKey, StringComparer.Ordinal)
            .Select(static group => group.MinBy(item => item.Distance)!.Distance)
            .ToArray();

        var stops500m = CountWithinRadius(allDistances, 500);
        var stops1km = CountWithinRadius(allDistances, 1000);
        var totalStops = stops1km;

        // ── 24-month mobility trend via Redis snapshot (T079) ─────────────────
        // Key stores total transit-stop count at last fetch; comparing against
        // current count approximates the 24-month snapshot-based trend.
        var snapshotKey = BuildSnapshotKey(address);
        var mobilityTrend = await ComputeMobilityTrendAsync(snapshotKey, totalStops, ct);

        return new ProviderFetchResult<PoiData>
        {
            Data = new PoiData
            {
                TransitStops500m = stops500m,
                TransitStops1km = stops1km,
                Pois2km = 0,
                Supermarkets1km = 0,
                Pharmacies1km = 0,
                Parks1km = 0,
                MobilityTrend = mobilityTrend,
            },
            RawPayload = BuildRawPayload(spTransBody, geoSampaBody),
        };
    }

    private async Task<string> GetBodyAsync(string requestUri, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(requestUri, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Transit provider request to '{requestUri}' failed with status {(int)response.StatusCode}.");
        }

        return body;
    }

    private static T Deserialize<T>(string body, string sourceName)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new InvalidOperationException($"{sourceName} response was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse {sourceName} transit payload.", ex);
        }
    }

    private static string BuildRequestPath(string path, PropertyAddress address, int radiusMetres)
    {
        var latitude = address.Lat!.Value.ToString(CultureInfo.InvariantCulture);
        var longitude = address.Lng!.Value.ToString(CultureInfo.InvariantCulture);
        return $"{path}?lat={latitude}&lng={longitude}&radius={radiusMetres}";
    }

    private static string BuildRawPayload(string spTransBody, string geoSampaBody)
    {
        var spTransPayload = JsonSerializer.Deserialize<JsonElement>(spTransBody);
        var geoSampaPayload = JsonSerializer.Deserialize<JsonElement>(geoSampaBody);

        return JsonSerializer.Serialize(new
        {
            sptrans = spTransPayload,
            geosampa = geoSampaPayload,
        });
    }

    private static int CountWithinRadius(IEnumerable<double> distances, double radiusMetres)
        => distances.Count(distance => distance <= radiusMetres);

    private static string NormalizeId(string? id, string? name)
        => string.IsNullOrWhiteSpace(id) ? name?.Trim() ?? Guid.NewGuid().ToString("N") : id.Trim();

    /// <summary>
    /// Derives transit snapshot key from provider name + coordinate hash so that
    /// geographically-close addresses do not share the same snapshot bucket.
    /// </summary>
    private static string BuildSnapshotKey(PropertyAddress address)
    {
        var lat = address.Lat!.Value.ToString("F6", CultureInfo.InvariantCulture);
        var lng = address.Lng!.Value.ToString("F6", CultureInfo.InvariantCulture);
        var input = $"sptrans_geosampa:{lat},{lng}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var hash = Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        return $"sptrans_geosampa:trend_snapshot:{hash}";
    }

    /// <summary>
    /// Reads the prior transit-stop snapshot from Redis, compares against the
    /// current count, derives <see cref="TrendDirection"/>, and persists the
    /// current count as the new snapshot.
    ///
    /// <para>Thresholds: &gt;10 % increase → Improving; &gt;10 % decrease → Worsening;
    /// otherwise Stable. When no prior snapshot exists, returns null (first fetch).</para>
    /// </summary>
    private async Task<TrendDirection?> ComputeMobilityTrendAsync(
        string snapshotKey,
        int currentStops,
        CancellationToken ct)
    {
        TrendDirection? trend = null;

        var prior = await _cacheService.GetAsync<TransitSnapshot>(snapshotKey, ct);
        if (prior is not null && prior.TotalStops > 0)
        {
            var ratio = currentStops / (double)prior.TotalStops;
            trend = ratio switch
            {
                > 1.10 => TrendDirection.Improving,
                < 0.90 => TrendDirection.Worsening,
                _ => TrendDirection.Stable,
            };

            _logger.LogInformation(
                "SpTransGeoSampaTransitProvider: MobilityTrend={Trend} (prior={Prior}, current={Current})",
                trend,
                prior.TotalStops,
                currentStops);
        }

        // Persist current snapshot for the next fetch cycle
        await _cacheService.SetAsync(
            snapshotKey,
            new TransitSnapshot { TotalStops = currentStops, RecordedAt = DateTime.UtcNow },
            SnapshotTtl,
            ct);

        return trend;
    }

    /// <summary>Persisted snapshot payload stored in Redis for trend comparison.</summary>
    private sealed class TransitSnapshot
    {
        public int TotalStops { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    private sealed record SpTransStopsResponse
    {
        public IReadOnlyList<TransitStop> Stops { get; init; } = [];
    }

    private sealed record GeoSampaStationsResponse
    {
        public IReadOnlyList<TransitStop> Stations { get; init; } = [];
    }

    private sealed record TransitStop
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        [JsonPropertyName("distance_m")]
        public double? DistanceMetres { get; init; }

        public string? Mode { get; init; }
    }

    private sealed record TransitObservation(string Source, string Identifier, double Distance)
    {
        public string UniqueKey => $"{Source}:{Identifier}";
    }
}
