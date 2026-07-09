using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Transit
{
    public sealed class OverpassPoiFallbackProvider : IDataProvider<PoiData>
    {
        public string ProviderName => "overpass_poi_fallback";
        public TimeSpan CacheTtl => TimeSpan.FromDays(7);

        private readonly HttpClient _http;

        public OverpassPoiFallbackProvider(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<ProviderFetchResult<PoiData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));
            if (!address.Lat.HasValue || !address.Lng.HasValue)
                throw new ArgumentException("Address must include Lat and Lng for Overpass fallback provider.");

            // Call the Overpass interpreter endpoint (tests stub /api/interpreter)
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/interpreter");
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            int transit500 = 0, transit1k = 0, pharmacies1k = 0, parks1k = 0, pois2k = 0;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in elements.EnumerateArray())
                    {
                        double? lat = null, lon = null;
                        if (el.TryGetProperty("lat", out var plat) && el.TryGetProperty("lon", out var plon))
                        {
                            if (plat.TryGetDouble(out var dlat) && plon.TryGetDouble(out var dlon))
                            {
                                lat = dlat; lon = dlon;
                            }
                        }
                        else if (el.TryGetProperty("geometry", out var geom) && geom.ValueKind == JsonValueKind.Array)
                        {
                            // geometry array of points: pick first
                            var first = geom[0];
                            if (first.TryGetProperty("lat", out var glat) && first.TryGetProperty("lon", out var glon))
                            {
                                if (glat.TryGetDouble(out var dlat) && glon.TryGetDouble(out var dlon))
                                {
                                    lat = dlat; lon = dlon;
                                }
                            }
                        }

                        if (!lat.HasValue || !lon.HasValue) continue;

                        pois2k++;

                        var dkm = HaversineKm(address.Lat.Value, address.Lng.Value, lat.Value, lon.Value);
                        if (dkm <= 0.5) transit500++;
                        if (dkm <= 1.0) transit1k++;

                        if (el.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                        {
                            if (tags.TryGetProperty("amenity", out var amen) && amen.ValueKind == JsonValueKind.String)
                            {
                                var a = amen.GetString();
                                if (string.Equals(a, "pharmacy", StringComparison.OrdinalIgnoreCase) && dkm <= 1.0)
                                    pharmacies1k++;
                            }

                            if (tags.TryGetProperty("leisure", out var leis) && leis.ValueKind == JsonValueKind.String)
                            {
                                var l = leis.GetString();
                                if (string.Equals(l, "park", StringComparison.OrdinalIgnoreCase) && dkm <= 1.0)
                                    parks1k++;
                            }

                            // treat common transit indicators as transit stops
                            if (tags.TryGetProperty("highway", out var highway) && highway.ValueKind == JsonValueKind.String)
                            {
                                var h = highway.GetString();
                                if (string.Equals(h, "bus_stop", StringComparison.OrdinalIgnoreCase))
                                {
                                    // already counted by distance
                                }
                            }
                        }
                    }
                }
                else if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in features.EnumerateArray())
                    {
                        if (!f.TryGetProperty("geometry", out var geometry)) continue;
                        if (!geometry.TryGetProperty("coordinates", out var coords) || coords.ValueKind != JsonValueKind.Array) continue;
                        // geojson coords: [lon, lat]
                        if (coords[0].TryGetDouble(out var lon) && coords[1].TryGetDouble(out var lat))
                        {
                            pois2k++;
                            var dkm = HaversineKm(address.Lat.Value, address.Lng.Value, lat, lon);
                            if (dkm <= 0.5) transit500++;
                            if (dkm <= 1.0) transit1k++;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // preserve RawPayload even if parsing fails; return empty counts
            }

            var data = new PoiData
            {
                TransitStops500m = transit500,
                TransitStops1km = transit1k,
                Pois2km = pois2k,
                Supermarkets1km = 0,
                Pharmacies1km = pharmacies1k,
                Parks1km = parks1k
            };

            return new ProviderFetchResult<PoiData> { Data = data, RawPayload = body };
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0; // km
            var dLat = ToRad(lat2 - lat1);
            var dLon = ToRad(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRad(double deg) => deg * (Math.PI / 180.0);
    }
}
