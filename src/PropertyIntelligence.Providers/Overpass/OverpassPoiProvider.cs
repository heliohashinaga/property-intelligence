using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Shared;

namespace PropertyIntelligence.Providers.Overpass;

/// <summary>
/// Fetches Points of Interest (POI) counts from OpenStreetMap via the Overpass API.
/// Implements <see cref="IDataProvider{PoiData}"/>.
/// </summary>
public sealed partial class OverpassPoiProvider : IDataProvider<PoiData>
{
    public string   ProviderName => "overpass";
    public TimeSpan CacheTtl     => TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory         _http;
    private readonly ICacheService              _cache;
    private readonly ILogger<OverpassPoiProvider> _logger;

    public OverpassPoiProvider(
        IHttpClientFactory           http,
        ICacheService                cache,
        ILogger<OverpassPoiProvider> logger)
    {
        _http   = http;
        _cache  = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PoiData> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        if (address.Lat is null || address.Lng is null)
        {
            LogNoCoordinates(_logger, address.NormalizedAddress);
            return new PoiData();
        }

        var cacheKey     = CacheKeyHelper.BuildKey(ProviderName, address.NormalizedAddress);
        var snapshotKey  = $"{cacheKey}:transit_snapshot";

        var cached = await _cache.GetAsync<PoiData>(cacheKey, ct).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        // Load the prior transit-stop snapshot for trend comparison (T079)
        var priorSnapshot = await _cache.GetAsync<TransitSnapshot>(snapshotKey, ct)
            .ConfigureAwait(false);

        var result = await FetchFromOverpassAsync(address.Lat.Value, address.Lng.Value, ct)
            .ConfigureAwait(false);

        // Compute 24-month mobility trend from snapshot delta
        var mobilityTrend = TrendCalculator.SnapshotDelta(
            prior:     priorSnapshot?.TransitStops1km,
            current:   result.TransitStops1km,
            tolerance: 2);

        result = result with { MobilityTrend = mobilityTrend };

        // Store new snapshot for future trend calculation (TTL matches cache TTL)
        await _cache.SetAsync(snapshotKey,
            new TransitSnapshot(result.TransitStops1km, DateTimeOffset.UtcNow),
            CacheTtl, ct).ConfigureAwait(false);

        await _cache.SetAsync(cacheKey, result, CacheTtl, ct).ConfigureAwait(false);
        return result;
    }

    // ── private ──────────────────────────────────────────────────────────────

    private async Task<PoiData> FetchFromOverpassAsync(double lat, double lng, CancellationToken ct)
    {
        var query = BuildOverpassQuery(lat, lng);
        var client = _http.CreateClient("overpass");

        var content  = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
        var response = await client.PostAsync("api/interpreter", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var overpassResponse = await response.Content
            .ReadFromJsonAsync<OverpassResponse>(_json, ct)
            .ConfigureAwait(false);

        if (overpassResponse is null)
            return new PoiData();

        return CountPois(overpassResponse.Elements, lat, lng);
    }

    private static string BuildOverpassQuery(double lat, double lng)
    {
        // Query for transit and amenity POIs within 2km radius
        // Tags: São Paulo metro uses railway=subway OR railway=station[station=subway] OR public_transport=platform
        //       Bus stops: highway=bus_stop OR public_transport=stop_position
        return $"""
            [out:json][timeout:25];
            (
              node["railway"="subway"](around:2000,{lat},{lng});
              way["railway"="subway"](around:2000,{lat},{lng});
              node["railway"="station"]["station"="subway"](around:2000,{lat},{lng});
              way["railway"="station"]["station"="subway"](around:2000,{lat},{lng});
              node["public_transport"="platform"]["train"!="yes"](around:2000,{lat},{lng});
              node["highway"="bus_stop"](around:2000,{lat},{lng});
              node["public_transport"="stop_position"](around:2000,{lat},{lng});
              node["amenity"="hospital"](around:2000,{lat},{lng});
              way["amenity"="hospital"](around:2000,{lat},{lng});
              node["amenity"="school"](around:2000,{lat},{lng});
              way["amenity"="school"](around:2000,{lat},{lng});
              node["amenity"="pharmacy"](around:2000,{lat},{lng});
              node["amenity"="clinic"](around:2000,{lat},{lng});
              node["amenity"="supermarket"](around:2000,{lat},{lng});
              way["amenity"="supermarket"](around:2000,{lat},{lng});
              node["leisure"="park"](around:2000,{lat},{lng});
              way["leisure"="park"](around:2000,{lat},{lng});
            );
            out center;
            """;
    }

    private static PoiData CountPois(List<OverpassElement> elements, double originLat, double originLng)
    {
        int transitStops500m = 0, transitStops1km = 0;
        int pois2km = 0;
        int supermarkets1km = 0;
        int pharmacies1km = 0;
        int parks1km = 0;
        bool hasFutureMetro = false;

        foreach (var el in elements)
        {
            var elLat = el.Lat ?? el.Center?.Lat;
            var elLng = el.Lon ?? el.Center?.Lon;
            if (elLat is null || elLng is null) continue;

            var dist = HaversineMetres(originLat, originLng, elLat.Value, elLng.Value);

            // Count all as "POI within 2km"
            if (dist <= 2000) pois2km++;

            // Transit (subway or bus_stop)
            var isTransit = (el.Tags.TryGetValue("railway", out var rw) && rw is "station" or "subway") ||
                            (el.Tags.TryGetValue("highway", out var hw) && hw == "bus_stop") ||
                            (el.Tags.TryGetValue("public_transport", out var pt) && pt is "platform" or "stop_position");
            if (isTransit)
            {
                if (dist <= 500) transitStops500m++;
                if (dist <= 1000) transitStops1km++;
            }

            // T047 — future metro station within 1km (construction=station tag)
            if (dist <= 1000 &&
                el.Tags.TryGetValue("construction", out var construction) &&
                construction == "station")
                hasFutureMetro = true;

            // Supermarkets within 1km
            if (el.Tags.TryGetValue("amenity", out var amenity) && amenity == "supermarket" && dist <= 1000)
                supermarkets1km++;

            // Pharmacies within 1km
            if (el.Tags.TryGetValue("amenity", out var am2) && am2 == "pharmacy" && dist <= 1000)
                pharmacies1km++;

            // Parks within 1km
            if (el.Tags.TryGetValue("leisure", out var leisure) && leisure == "park" && dist <= 1000)
                parks1km++;
        }

        return new PoiData
        {
            TransitStops500m = transitStops500m,
            TransitStops1km  = transitStops1km,
            Pois2km          = pois2km,
            Supermarkets1km  = supermarkets1km,
            Pharmacies1km    = pharmacies1km,
            Parks1km         = parks1km,
            HasFutureMetro   = hasFutureMetro,
        };
    }

    /// <summary>Haversine distance in metres between two WGS84 points.</summary>
    private static double HaversineMetres(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6_371_000; // Earth radius in metres
        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "OverpassPoiProvider: coordinates missing for '{Address}'; returning empty PoiData")]
    private static partial void LogNoCoordinates(ILogger logger, string address);
}

/// <summary>Serialised snapshot of transit-stop count stored in Redis for mobility trend calculation.</summary>
internal sealed record TransitSnapshot(
    int              TransitStops1km,
    DateTimeOffset   StoredAt);
