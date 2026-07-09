using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Transit
{
    public sealed class SpTransGeoSampaTransitProvider : IDataProvider<PoiData>
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClient? _overpassClient;
        private readonly TimeSpan _cacheTtl = TimeSpan.FromDays(7);

        public SpTransGeoSampaTransitProvider(HttpClient httpClient, HttpClient? overpassClient = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _overpassClient = overpassClient;
        }

        public string ProviderName => "sptrans_geosampa_transit";

        public TimeSpan CacheTtl => _cacheTtl;

        public async Task<ProviderFetchResult<PoiData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        {
            // Call official CSV endpoint
            var resp = await _httpClient.GetAsync("/sptrans/stops.csv", ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            // Parse CSV simple: header then lines
            var lines = body.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var stops = lines.Skip(1)
                .Select(l => l.Split(','))
                .Where(parts => parts.Length >= 4)
                .Select(parts => new {
                    Id = parts[0].Trim(),
                    Name = parts[1].Trim(),
                    Lat = ParseDoubleOrNull(parts[2]),
                    Lng = ParseDoubleOrNull(parts[3])
                })
                .ToArray();

            int within500m = 0;
            int within1km = 0;

            if (address.Lat.HasValue && address.Lng.HasValue)
            {
                foreach (var s in stops)
                {
                    if (!s.Lat.HasValue || !s.Lng.HasValue) continue;
                    var d = HaversineDistanceKm(address.Lat.Value, address.Lng.Value, s.Lat.Value, s.Lng.Value);
                    if (d <= 0.5) within500m++;
                    if (d <= 1.0) within1km++;
                }
            }

            var poi = new PoiData
            {
                TransitStops500m = within500m,
                TransitStops1km = within1km,
                Pois2km = 0,
                Supermarkets1km = 0,
                Pharmacies1km = 0,
                Parks1km = 0,
                MobilityTrend = null
            };

            return new ProviderFetchResult<PoiData>
            {
                Data = poi,
                RawPayload = body
            };
        }

        private static double? ParseDoubleOrNull(string s)
        {
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out v)) return v;
            return null;
        }

        // Haversine distance in km
        private static double HaversineDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // km
            var dLat = ToRad(lat2 - lat1);
            var dLon = ToRad(lon2 - lon1);
            var a = Math.Sin(dLat/2) * Math.Sin(dLat/2) + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon/2) * Math.Sin(dLon/2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1-a));
            return R * c;
        }

        private static double ToRad(double deg) => deg * Math.PI / 180.0;
    }
}
