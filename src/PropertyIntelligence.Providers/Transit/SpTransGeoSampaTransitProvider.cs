using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheTtl;

    public SpTransGeoSampaTransitProvider(HttpClient httpClient, TimeSpan cacheTtl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheTtl = cacheTtl;
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

        return new ProviderFetchResult<PoiData>
        {
            Data = new PoiData
            {
                TransitStops500m = CountWithinRadius(allDistances, 500),
                TransitStops1km = CountWithinRadius(allDistances, 1000),
                Pois2km = 0,
                Supermarkets1km = 0,
                Pharmacies1km = 0,
                Parks1km = 0,
                MobilityTrend = null,
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
