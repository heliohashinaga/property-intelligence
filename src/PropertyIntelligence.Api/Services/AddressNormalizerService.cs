using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Api.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Api.Services;

/// <summary>
/// Normalizes a raw free-text Brazilian address into a structured <see cref="PropertyAddress"/>.
/// Strategy:
///   1. If a CEP pattern is found → delegate to <see cref="ViaCepProvider"/>.
///   2. Otherwise → query Nominatim (OpenStreetMap geocoder).
/// Returns <c>null</c> when the address cannot be resolved (caller should return HTTP 422).
/// </summary>
public sealed partial class AddressNormalizerService : IAddressNormalizer
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IDataProvider<PropertyAddress>    _viaCep;
    private readonly IHttpClientFactory               _http;
    private readonly ILogger<AddressNormalizerService> _logger;

    public AddressNormalizerService(
        IDataProvider<PropertyAddress>    viaCep,
        IHttpClientFactory                http,
        ILogger<AddressNormalizerService> logger)
    {
        _viaCep = viaCep;
        _http   = http;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PropertyAddress?> NormalizeAsync(string rawAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            return null;

        // 1. Try CEP-based normalization via ViaCEP
        var cepMatch = Regex.Match(rawAddress, @"\b(\d{5})-?(\d{3})\b");
        if (cepMatch.Success)
        {
            var cep = cepMatch.Groups[1].Value + cepMatch.Groups[2].Value;
            try
            {
                var stub = new PropertyAddress
                {
                    NormalizedAddress = rawAddress,
                    PostalCode        = cep,
                    City              = string.Empty,
                    State             = string.Empty,
                };
                return await _viaCep.FetchAsync(stub, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogViaCepFailed(_logger, ex, cep);
            }
        }

        // 2. Fallback: Nominatim free-text geocoding
        return await NormalizeViaNominatimAsync(rawAddress, ct).ConfigureAwait(false);
    }

    // ── private ──────────────────────────────────────────────────────────────

    private async Task<PropertyAddress?> NormalizeViaNominatimAsync(string rawAddress, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient("nominatim");
            var url    = $"search?q={Uri.EscapeDataString(rawAddress)}&format=json&countrycodes=br&addressdetails=1&limit=5";

            var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
                cancellationToken: ct).ConfigureAwait(false);

            var root = doc.RootElement;
            if (root.GetArrayLength() == 0)
            {
                LogNominatimNoResults(_logger, AddressLogEnricher.Hash(rawAddress));
                return null;
            }

            var first = root[0];
            return ParseNominatimResult(first);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNominatimFailed(_logger, ex, AddressLogEnricher.Hash(rawAddress));
            return null;
        }
    }

    private static PropertyAddress? ParseNominatimResult(JsonElement element)
    {
        if (!element.TryGetProperty("lat", out var latEl) ||
            !element.TryGetProperty("lon", out var lngEl))
            return null;

        if (!double.TryParse(latEl.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(lngEl.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lng))
            return null;

        var displayName = element.TryGetProperty("display_name", out var dn)
            ? dn.GetString() ?? string.Empty
            : string.Empty;

        // Parse addressdetails
        string? road = null, houseNumber = null, suburb = null, city = null, state = null, postcode = null;
        if (element.TryGetProperty("address", out var addr))
        {
            road        = GetString(addr, "road");
            houseNumber = GetString(addr, "house_number");
            suburb      = GetString(addr, "suburb") ?? GetString(addr, "neighbourhood");
            city        = GetString(addr, "city") ?? GetString(addr, "town") ?? GetString(addr, "municipality");
            state       = GetString(addr, "state");
            postcode    = GetString(addr, "postcode")?.Replace("-", "");
        }

        return new PropertyAddress
        {
            NormalizedAddress = displayName,
            StreetName        = road,
            StreetNumber      = houseNumber,
            Neighborhood      = suburb,
            City              = city  ?? string.Empty,
            State             = state ?? string.Empty,
            PostalCode        = postcode,
            Lat               = lat,
            Lng               = lng,
        };
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var prop) ? prop.GetString() : null;

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ViaCEP lookup failed for CEP {Cep}; falling back to Nominatim")]
    private static partial void LogViaCepFailed(ILogger logger, Exception ex, string cep);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Nominatim returned no results for '{RawAddress}'")]
    private static partial void LogNominatimNoResults(ILogger logger, string rawAddress);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Nominatim geocoding failed for '{RawAddress}'")]
    private static partial void LogNominatimFailed(ILogger logger, Exception ex, string rawAddress);
}
