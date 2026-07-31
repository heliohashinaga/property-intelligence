using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.GeoSampa;

/// <summary>
/// GeoSampa zoning and cadastral data provider — queries official São Paulo
/// municipal zoning layers and cadastral database to extract zoning classification
/// and property valuation (valor venal) for appreciation scoring.
/// 
/// <para>Source: GeoSampa / Prefeitura Municipal de São Paulo</para>
/// <para>Endpoint: geosampa.prefeitura.sp.gov.br/geoserver/wfs or equivalent REST API</para>
/// <para>Cache: 30 days</para>
/// </summary>
public sealed class GeoSampaZoneamentoProvider : IDataProvider<IptuData>
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeoSampaZoneamentoProvider> _logger;
    private readonly TimeSpan _cacheTtl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public string ProviderName => "geosampa_zoneamento";

    public TimeSpan CacheTtl => _cacheTtl;

    public GeoSampaZoneamentoProvider(
        HttpClient httpClient,
        ILogger<GeoSampaZoneamentoProvider> logger,
        TimeSpan cacheTtl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl;
    }

    /// <summary>
    /// Fetches zoning and cadastral data from GeoSampa for the given address.
    /// Queries the zoneamento layer to find the zoning classification and cadastral
    /// value (valor venal) for appreciation scoring.
    /// </summary>
    public async Task<ProviderFetchResult<IptuData>> FetchAsync(
        PropertyAddress address,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (!address.Lat.HasValue || !address.Lng.HasValue)
        {
            _logger.LogWarning(
                "GeoSampaZoneamentoProvider: Address missing coordinates. Cannot query zoning data. Address={NormalizedAddress}",
                address.NormalizedAddress);

            return new ProviderFetchResult<IptuData>
            {
                Data = new IptuData
                {
                    ZoningClass = null,
                    ValorVenal = null,
                    AppreciationTrend = null,
                },
                RawPayload = JsonSerializer.Serialize(new { error = "missing_coordinates" }),
            };
        }

        var lat = address.Lat.Value;
        var lng = address.Lng.Value;

        // Query GeoSampa WFS endpoint for zoning at this point
        // Example WFS request (actual endpoint may vary):
        // GET /geosampa/wfs?
        //   service=WFS&
        //   version=2.0.0&
        //   request=GetFeature&
        //   typeName=geosampa:zoneamento&
        //   outputFormat=application/json&
        //   cql_filter=INTERSECTS(geom,POINT({lng} {lat}))

        var requestPath = BuildWfsRequestPath(lng, lat);

        GeoSampaZoneamentoResponse? response;
        try
        {
            _logger.LogDebug(
                "GeoSampaZoneamentoProvider: Querying WFS endpoint. Path={RequestPath}",
                requestPath);

            response = await _httpClient.GetFromJsonAsync<GeoSampaZoneamentoResponse>(
                requestPath,
                JsonOptions,
                ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "GeoSampaZoneamentoProvider: HTTP request failed. Address={NormalizedAddress}",
                address.NormalizedAddress);

            return new ProviderFetchResult<IptuData>
            {
                Data = new IptuData
                {
                    ZoningClass = null,
                    ValorVenal = null,
                    AppreciationTrend = null,
                },
                RawPayload = JsonSerializer.Serialize(new { error = "http_request_failed", message = ex.Message }),
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "GeoSampaZoneamentoProvider: Failed to parse JSON response. Address={NormalizedAddress}",
                address.NormalizedAddress);

            return new ProviderFetchResult<IptuData>
            {
                Data = new IptuData
                {
                    ZoningClass = null,
                    ValorVenal = null,
                    AppreciationTrend = null,
                },
                RawPayload = JsonSerializer.Serialize(new { error = "json_parse_failed", message = ex.Message }),
            };
        }

        if (response?.Zones is null || response.Zones.Count == 0)
        {
            _logger.LogInformation(
                "GeoSampaZoneamentoProvider: No zoning data found for coordinates. Lat={Lat}, Lng={Lng}",
                lat,
                lng);

            var emptyPayload = response != null 
                ? JsonSerializer.Serialize(response) 
                : JsonSerializer.Serialize(new { zones = Array.Empty<object>() });
            
            return new ProviderFetchResult<IptuData>
            {
                Data = new IptuData
                {
                    ZoningClass = null,
                    ValorVenal = null,
                    AppreciationTrend = null,
                },
                RawPayload = emptyPayload,
            };
        }

        // Extract zoning class from the first matching zone
        var zone = response.Zones.First();
        var zoningClass = zone.ZoneCode;

        // Extract cadastral valor venal if available
        decimal? valorVenal = response.Cadastro?.ValorVenal;

        var iptuData = new IptuData
        {
            ZoningClass = zoningClass,
            ValorVenal = valorVenal,
            // TODO: derive AppreciationTrend from multi-snapshot GeoSampa history once 2+ import snapshots available (T043)
            AppreciationTrend = TrendDirection.Stable,
            FutureTransitDistanceMetres = zone.FutureTransitDistanceMetres,
            ZoningPermissivenessScore = zone.PermissivenessScore,
        };

        var rawPayload = JsonSerializer.Serialize(response);

        _logger.LogInformation(
            "GeoSampaZoneamentoProvider: Successfully fetched zoning data. Address={NormalizedAddress}, ZoningClass={ZoningClass}, ValorVenal={ValorVenal}",
            address.NormalizedAddress,
            zoningClass,
            valorVenal);

        return new ProviderFetchResult<IptuData>
        {
            Data = iptuData,
            RawPayload = rawPayload,
        };
    }

    private static string BuildWfsRequestPath(double lng, double lat)
    {
        // Build WFS GetFeature request for zoning layer
        // The actual GeoSampa WFS endpoint structure may vary; this is a plausible pattern
        return $"/geosampa/wfs?" +
               $"service=WFS&" +
               $"version=2.0.0&" +
               $"request=GetFeature&" +
               $"typeName=geosampa:zoneamento&" +
               $"outputFormat=application/json&" +
               $"cql_filter=INTERSECTS(geom,POINT({lng:F6} {lat:F6}))";
    }

    /// <summary>
    /// DTO for GeoSampa zoneamento WFS response.
    /// </summary>
    private sealed class GeoSampaZoneamentoResponse
    {
        [JsonPropertyName("zones")]
        public List<ZoneInfo>? Zones { get; set; }

        [JsonPropertyName("cadastro")]
        public CadastroInfo? Cadastro { get; set; }
    }

    private sealed class ZoneInfo
    {
        [JsonPropertyName("zone_code")]
        public string? ZoneCode { get; set; }

        [JsonPropertyName("permissiveness_score")]
        public int? PermissivenessScore { get; set; }

        [JsonPropertyName("future_transit_distance_metres")]
        public double? FutureTransitDistanceMetres { get; set; }
    }

    private sealed class CadastroInfo
    {
        [JsonPropertyName("valor_venal")]
        public decimal? ValorVenal { get; set; }

        [JsonPropertyName("fiscal_year")]
        public int? FiscalYear { get; set; }
    }
}
