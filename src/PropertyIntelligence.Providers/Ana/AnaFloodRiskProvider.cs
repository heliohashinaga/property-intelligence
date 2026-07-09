using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Ana
{
    /// <summary>
    /// ANA SNIRH flood risk provider.
    /// Supports a test-friendly delegate-based executor to avoid real DB calls in tests.
    /// </summary>
    public sealed class AnaFloodRiskProvider : IDataProvider<FloodRiskData>
    {
        public string ProviderName => "ana_snirh";
        public TimeSpan CacheTtl => TimeSpan.FromDays(30);

        private readonly Func<double, double, CancellationToken, Task<string>> _executor;

        // Constructor for tests / injected executor
        public AnaFloodRiskProvider(Func<double, double, CancellationToken, Task<string>> executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        // Future constructor for production could accept a connection string and run the SQL against PostGIS.

        public async Task<ProviderFetchResult<FloodRiskData>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));
            if (!address.Lat.HasValue || !address.Lng.HasValue)
                throw new InvalidOperationException("Coordinates (Lat/Lng) are required for ANA flood risk provider.");

            // Execute the parameterized PostGIS query via the provided executor delegate.
            // The executor is expected to return a JSON string representing an array of zones with 'severity' and 'distance_metres'.
            var body = await _executor(address.Lat.Value, address.Lng.Value, ct).ConfigureAwait(false) ?? string.Empty;

            string? highest = null;
            double? distance = null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        var severity = item.TryGetProperty("severity", out var sval) && sval.ValueKind == JsonValueKind.String ? sval.GetString() : null;
                        var dist = item.TryGetProperty("distance_metres", out var dval) && dval.TryGetDouble(out var d) ? d : (double?)null;

                        if (severity is null) continue;
                        var sNorm = severity.Trim().ToLowerInvariant();

                        if (highest is null)
                        {
                            highest = sNorm;
                            distance = dist;
                        }
                        else
                        {
                            // choose the more severe
                            if (SeverityRank(sNorm) > SeverityRank(highest))
                            {
                                highest = sNorm;
                                distance = dist;
                            }
                            else if (SeverityRank(sNorm) == SeverityRank(highest) && dist.HasValue)
                            {
                                // keep minimum distance
                                if (!distance.HasValue || dist.Value < distance.Value) distance = dist.Value;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // preserve RawPayload but return null RiskLevel
            }

            var data = new FloodRiskData
            {
                RiskLevel = highest,
                DistanceMetres = distance,
                Trend = TrendDirection.Stable
            };

            return new ProviderFetchResult<FloodRiskData> { Data = data, RawPayload = body };
        }

        private static int SeverityRank(string severity) => severity switch
        {
            "low" => 1,
            "moderate" => 2,
            "high" => 3,
            "critical" => 4,
            _ => 0
        };
    }
}
