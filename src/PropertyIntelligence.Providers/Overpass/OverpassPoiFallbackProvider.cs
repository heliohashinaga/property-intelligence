using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Overpass;

public sealed class OverpassPoiFallbackProvider : IDataProvider<PoiData>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheTtl;

    public OverpassPoiFallbackProvider(HttpClient httpClient, TimeSpan cacheTtl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheTtl = cacheTtl;
    }

    public string ProviderName => "overpass";

    public TimeSpan CacheTtl => _cacheTtl;

    public async Task<ProviderFetchResult<PoiData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (!address.Lat.HasValue || !address.Lng.HasValue)
        {
            throw new InvalidOperationException("Overpass fallback queries require address coordinates.");
        }

        var query = BuildQuery(address.Lat.Value, address.Lng.Value);
        using var response = await _httpClient.PostAsync(
            "api/interpreter",
            new StringContent(query, Encoding.UTF8, "text/plain"),
            ct);

        var rawPayload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Overpass request failed with status {(int)response.StatusCode}.");
        }

        var payload = Deserialize(rawPayload);
        var observations = payload.Elements
            .Select(element => element.ToObservation(address.Lat.Value, address.Lng.Value))
            .Where(static observation => observation is not null)
            .Select(static observation => observation!)
            .ToArray();

        return new ProviderFetchResult<PoiData>
        {
            Data = new PoiData
            {
                TransitStops500m = observations.Count(IsTransitWithin(500)),
                TransitStops1km = observations.Count(IsTransitWithin(1000)),
                Pois2km = observations.Count(static observation => observation.DistanceMetres <= 2000),
                Supermarkets1km = observations.Count(static observation => observation.DistanceMetres <= 1000 && observation.IsSupermarket),
                Pharmacies1km = observations.Count(static observation => observation.DistanceMetres <= 1000 && observation.IsPharmacy),
                Parks1km = observations.Count(static observation => observation.DistanceMetres <= 1000 && observation.IsPark),
                MobilityTrend = null,
            },
            RawPayload = rawPayload,
        };
    }

    private static Func<OverpassObservation, bool> IsTransitWithin(double radiusMetres)
        => observation => observation.DistanceMetres <= radiusMetres && observation.IsTransitStop;

    private static OverpassResponse Deserialize(string rawPayload)
    {
        try
        {
            return JsonSerializer.Deserialize<OverpassResponse>(rawPayload, JsonOptions)
                ?? throw new InvalidOperationException("Overpass response was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse Overpass payload.", ex);
        }
    }

    private static string BuildQuery(double latitude, double longitude)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lng = longitude.ToString(CultureInfo.InvariantCulture);

        return $$"""
            [out:json][timeout:10];
            (
              node["highway"="bus_stop"](around:500,{{lat}},{{lng}});
              node["railway"="station"](around:500,{{lat}},{{lng}});
              node["subway"="yes"](around:500,{{lat}},{{lng}});
              node["highway"="bus_stop"](around:1000,{{lat}},{{lng}});
              node["railway"="station"](around:1000,{{lat}},{{lng}});
              node["subway"="yes"](around:1000,{{lat}},{{lng}});
              node["shop"="supermarket"](around:1000,{{lat}},{{lng}});
              node["amenity"="pharmacy"](around:1000,{{lat}},{{lng}});
              node["leisure"="park"](around:1000,{{lat}},{{lng}});
              node["amenity"](around:2000,{{lat}},{{lng}});
              node["highway"="bus_stop"](around:2000,{{lat}},{{lng}});
              node["railway"="station"](around:2000,{{lat}},{{lng}});
              node["shop"="supermarket"](around:2000,{{lat}},{{lng}});
              node["amenity"="pharmacy"](around:2000,{{lat}},{{lng}});
              node["leisure"="park"](around:2000,{{lat}},{{lng}});
            );
            out body;
            """;
    }

    private static double CalculateDistanceMetres(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusMetres = 6371000d;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLng = DegreesToRadians(lng2 - lng1);
        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
            Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMetres * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

    private sealed record OverpassResponse
    {
        public IReadOnlyList<OverpassElement> Elements { get; init; } = [];
    }

    private sealed record OverpassElement
    {
        public string? Type { get; init; }
        public long Id { get; init; }
        public double? Lat { get; init; }
        public double? Lon { get; init; }
        public OverpassCenter? Center { get; init; }
        public Dictionary<string, string> Tags { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public OverpassObservation? ToObservation(double addressLat, double addressLng)
        {
            var lat = Lat ?? Center?.Lat;
            var lng = Lon ?? Center?.Lon;

            if (!lat.HasValue || !lng.HasValue)
            {
                return null;
            }

            var distance = CalculateDistanceMetres(addressLat, addressLng, lat.Value, lng.Value);
            return new OverpassObservation(
                Id,
                distance,
                IsTransitStop(Tags),
                IsSupermarket(Tags),
                IsPharmacy(Tags),
                IsPark(Tags));
        }
    }

    private sealed record OverpassCenter
    {
        public double Lat { get; init; }
        public double Lon { get; init; }
    }

    private sealed record OverpassObservation(
        long Id,
        double DistanceMetres,
        bool IsTransitStop,
        bool IsSupermarket,
        bool IsPharmacy,
        bool IsPark);

    private static bool IsTransitStop(IReadOnlyDictionary<string, string> tags)
        => HasTag(tags, "highway", "bus_stop")
           || HasTag(tags, "railway", "station")
           || HasTag(tags, "subway", "yes");

    private static bool IsSupermarket(IReadOnlyDictionary<string, string> tags)
        => HasTag(tags, "shop", "supermarket");

    private static bool IsPharmacy(IReadOnlyDictionary<string, string> tags)
        => HasTag(tags, "amenity", "pharmacy");

    private static bool IsPark(IReadOnlyDictionary<string, string> tags)
        => HasTag(tags, "leisure", "park");

    private static bool HasTag(IReadOnlyDictionary<string, string> tags, string key, string value)
        => tags.TryGetValue(key, out var actual) && string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);
}
