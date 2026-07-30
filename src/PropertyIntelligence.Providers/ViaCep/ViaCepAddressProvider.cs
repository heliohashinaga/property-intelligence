using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.ViaCep
{
    /// <summary>
    /// ViaCEP-backed address provider with municipality/state validation and
    /// best-effort coordinate resolution.
    /// </summary>
    public sealed class ViaCepAddressProvider : IDataProvider<AddressInfo>
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly HttpClient _httpClient;
        private readonly TimeSpan _cacheTtl;
        private readonly HttpClient? _saoPauloGeocodingHttpClient;
        private readonly HttpClient? _nominatimHttpClient;

        public ViaCepAddressProvider(HttpClient httpClient, TimeSpan cacheTtl)
            : this(httpClient, cacheTtl, null, null)
        {
        }

        public ViaCepAddressProvider(
            HttpClient httpClient,
            TimeSpan cacheTtl,
            HttpClient? saoPauloGeocodingHttpClient,
            HttpClient? nominatimHttpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _cacheTtl = cacheTtl;
            _saoPauloGeocodingHttpClient = saoPauloGeocodingHttpClient;
            _nominatimHttpClient = nominatimHttpClient;
        }

        /// <inheritdoc />
        public string ProviderName => "viacep";

        /// <inheritdoc />
        public TimeSpan CacheTtl => _cacheTtl;

        /// <summary>
        /// Fetches address data from ViaCEP using the provided PostalCode (CEP).
        /// Returns a <see cref="ProviderFetchResult{AddressInfo}"/> containing
        /// the typed AddressInfo and the raw JSON payload captured from ViaCEP.
        ///
        /// Behavior:
        /// - If address.PostalCode is provided, call /ws/{cep}/json/ on the
        ///   configured HttpClient BaseAddress.
        /// - If ViaCEP returns 200 with a JSON body, parse fields and return
        ///   AddressInfo. If ViaCEP returns an error or the body contains
        ///   "erro": true, throw an exception so callers can mark the provider
        ///   as unavailable.
        /// - If no PostalCode is available, return a best-effort AddressInfo
        ///   populated from the incoming normalized address and do NOT call
        ///   the external service (MVP-friendly fallback).
        /// </summary>
        public async Task<ProviderFetchResult<AddressInfo>> FetchAsync(PropertyAddress address, CancellationToken ct = default)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));

            // Best-effort fallback when no CEP provided: return minimal AddressInfo
            if (string.IsNullOrWhiteSpace(address.PostalCode))
            {
                return new ProviderFetchResult<AddressInfo>
                {
                    Data = BuildAddressInfo(
                        address,
                        normalizedAddress: address.NormalizedAddress,
                        streetName: address.StreetName,
                        neighborhood: address.Neighborhood,
                        city: address.City,
                        state: address.State,
                        postalCode: address.PostalCode,
                        lat: address.Lat,
                        lng: address.Lng),
                    RawPayload = "{}",
                };
            }

            // Normalize CEP to 8 digits (remove non-digits)
            var cep = Regex.Replace(address.PostalCode, "\\D", string.Empty);
            if (cep.Length != 8) throw new InvalidOperationException($"Invalid CEP format: '{address.PostalCode}'");

            // Build request path: /ws/{cep}/json/
            var path = $"/ws/{cep}/json/".TrimStart('/');

            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"ViaCEP request failed with status {(int)resp.StatusCode}");
            }

            try
            {
                var viaCep = JsonSerializer.Deserialize<ViaCepResponse>(body, JsonOptions)
                    ?? throw new InvalidOperationException("ViaCEP returned an empty response.");

                if (viaCep.Erro)
                {
                    throw new InvalidOperationException($"CEP {cep} not found in ViaCEP");
                }

                ValidateMunicipalityAndState(address, viaCep);
                var coordinates = await ResolveCoordinatesAsync(address, viaCep, cep, ct);
                var streetName = FirstNonEmpty(viaCep.Logradouro, address.StreetName);
                var neighborhood = FirstNonEmpty(viaCep.Bairro, address.Neighborhood);
                var city = FirstNonEmpty(viaCep.Localidade, address.City)
                    ?? throw new InvalidOperationException("ViaCEP response missing localidade.");
                var state = FirstNonEmpty(viaCep.Uf, address.State)
                    ?? throw new InvalidOperationException("ViaCEP response missing uf.");
                var postalCode = FirstNonEmpty(viaCep.Cep, address.PostalCode);
                var normalized = ComposeNormalizedAddress(
                    streetName,
                    address.StreetNumber,
                    neighborhood,
                    city,
                    state,
                    postalCode,
                    address.NormalizedAddress);

                return new ProviderFetchResult<AddressInfo>
                {
                    Data = BuildAddressInfo(
                        address,
                        normalized,
                        streetName,
                        neighborhood,
                        city,
                        state,
                        postalCode,
                        coordinates.Latitude,
                        coordinates.Longitude),
                    RawPayload = body,
                };
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Failed to parse ViaCEP response", ex);
            }
        }

        private static AddressInfo BuildAddressInfo(
            PropertyAddress address,
            string normalizedAddress,
            string? streetName,
            string? neighborhood,
            string city,
            string state,
            string? postalCode,
            double? lat,
            double? lng) => new()
            {
                NormalizedAddress = normalizedAddress,
                StreetName = streetName,
                StreetNumber = address.StreetNumber,
                Neighborhood = neighborhood,
                City = city,
                State = state,
                PostalCode = postalCode,
                Lat = lat,
                Lng = lng,
            };

        private static void ValidateMunicipalityAndState(PropertyAddress address, ViaCepResponse viaCep)
        {
            if (string.IsNullOrWhiteSpace(viaCep.Localidade) || string.IsNullOrWhiteSpace(viaCep.Uf))
            {
                throw new InvalidOperationException("ViaCEP response missing required localidade or uf fields.");
            }

            var stateMatches = string.Equals(
                NormalizeComparable(address.State),
                NormalizeComparable(viaCep.Uf),
                StringComparison.Ordinal);

            var cityMatches = string.Equals(
                NormalizeComparable(address.City),
                NormalizeComparable(viaCep.Localidade),
                StringComparison.Ordinal);

            if (!cityMatches || !stateMatches)
            {
                throw new InvalidOperationException(
                    $"ViaCEP localidade/uf mismatch for requested address. " +
                    $"Requested={address.City}/{address.State}; Returned={viaCep.Localidade}/{viaCep.Uf}.");
            }
        }

        private async Task<(double? Latitude, double? Longitude)> ResolveCoordinatesAsync(
            PropertyAddress address,
            ViaCepResponse viaCep,
            string normalizedCep,
            CancellationToken ct)
        {
            if (address.Lat.HasValue && address.Lng.HasValue)
            {
                return (address.Lat, address.Lng);
            }

            var query = BuildGeocodingQuery(address, viaCep, normalizedCep);

            if (IsSaoPauloAddress(viaCep) && _saoPauloGeocodingHttpClient is not null)
            {
                var saoPauloCoordinates = await TryResolveCoordinatesAsync(
                    _saoPauloGeocodingHttpClient,
                    [
                        $"api/geocode?address={Uri.EscapeDataString(query)}&postalCode={normalizedCep}&city=S%C3%A3o%20Paulo&state=SP",
                        $"search?format=jsonv2&limit=1&countrycodes=br&postalcode={normalizedCep}&city=S%C3%A3o%20Paulo&q={Uri.EscapeDataString(query)}",
                    ],
                    addNominatimHeaders: false,
                    ct);

                if (saoPauloCoordinates is not null)
                {
                    return saoPauloCoordinates.Value;
                }
            }

            if (_nominatimHttpClient is not null)
            {
                var fallbackCoordinates = await TryResolveCoordinatesAsync(
                    _nominatimHttpClient,
                    [$"search?format=jsonv2&limit=1&countrycodes=br&postalcode={normalizedCep}&q={Uri.EscapeDataString(query)}"],
                    addNominatimHeaders: true,
                    ct);

                if (fallbackCoordinates is not null)
                {
                    return fallbackCoordinates.Value;
                }
            }

            return (null, null);
        }

        private static async Task<(double Latitude, double Longitude)?> TryResolveCoordinatesAsync(
            HttpClient httpClient,
            IReadOnlyList<string> requestUris,
            bool addNominatimHeaders,
            CancellationToken ct)
        {
            foreach (var requestUri in requestUris)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                if (addNominatimHeaders)
                {
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PropertyIntelligence", "1.0"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                }

                try
                {
                    using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var body = await response.Content.ReadAsStringAsync(ct);
                    if (TryParseCoordinates(body, out var latitude, out var longitude))
                    {
                        return (latitude, longitude);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException)
                {
                    continue;
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            return null;
        }

        private static bool TryParseCoordinates(string body, out double latitude, out double longitude)
        {
            using var document = JsonDocument.Parse(body);
            return TryParseCoordinates(document.RootElement, out latitude, out longitude);
        }

        private static bool TryParseCoordinates(JsonElement element, out double latitude, out double longitude)
        {
            if (TryReadLatLng(element, out latitude, out longitude))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryParseCoordinates(item, out latitude, out longitude))
                    {
                        return true;
                    }
                }
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("results", out var results)
                    && TryParseCoordinates(results, out latitude, out longitude))
                {
                    return true;
                }

                if (element.TryGetProperty("features", out var features)
                    && TryParseCoordinates(features, out latitude, out longitude))
                {
                    return true;
                }

                if (element.TryGetProperty("geometry", out var geometry))
                {
                    if (geometry.TryGetProperty("coordinates", out var coordinates)
                        && coordinates.ValueKind == JsonValueKind.Array
                        && coordinates.GetArrayLength() >= 2
                        && TryReadDouble(coordinates[0], out var lng)
                        && TryReadDouble(coordinates[1], out var lat))
                    {
                        latitude = lat;
                        longitude = lng;
                        return true;
                    }

                    if (TryParseCoordinates(geometry, out latitude, out longitude))
                    {
                        return true;
                    }
                }

                if (element.TryGetProperty("location", out var location)
                    && TryParseCoordinates(location, out latitude, out longitude))
                {
                    return true;
                }
            }

            latitude = default;
            longitude = default;
            return false;
        }

        private static bool TryReadLatLng(JsonElement element, out double latitude, out double longitude)
        {
            latitude = default;
            longitude = default;

            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hasLatitude = TryGetPropertyIgnoreCase(element, "lat", out var latElement)
                || TryGetPropertyIgnoreCase(element, "latitude", out latElement);
            var hasLongitude = TryGetPropertyIgnoreCase(element, "lon", out var lngElement)
                || TryGetPropertyIgnoreCase(element, "lng", out lngElement)
                || TryGetPropertyIgnoreCase(element, "longitude", out lngElement);

            if (!hasLatitude || !hasLongitude)
            {
                return false;
            }

            if (!TryReadDouble(latElement, out latitude) || !TryReadDouble(lngElement, out longitude))
            {
                return false;
            }

            return true;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static bool TryReadDouble(JsonElement element, out double value)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.TryGetDouble(out value);
            }

            if (element.ValueKind == JsonValueKind.String
                && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        private static string BuildGeocodingQuery(PropertyAddress address, ViaCepResponse viaCep, string normalizedCep)
        {
            var street = FirstNonEmpty(viaCep.Logradouro, address.StreetName);
            var neighborhood = FirstNonEmpty(viaCep.Bairro, address.Neighborhood);
            var city = FirstNonEmpty(viaCep.Localidade, address.City);
            var state = FirstNonEmpty(viaCep.Uf, address.State);

            var builder = new StringBuilder();
            AppendSegment(builder, street is null
                ? null
                : string.IsNullOrWhiteSpace(address.StreetNumber) ? street : $"{street}, {address.StreetNumber}");
            AppendSegment(builder, neighborhood);
            AppendSegment(builder, city);
            AppendSegment(builder, state);
            AppendSegment(builder, normalizedCep);

            return builder.ToString();
        }

        private static string ComposeNormalizedAddress(
            string? streetName,
            string? streetNumber,
            string? neighborhood,
            string city,
            string state,
            string? postalCode,
            string fallback)
        {
            var builder = new StringBuilder();
            AppendSegment(builder, streetName is null
                ? null
                : string.IsNullOrWhiteSpace(streetNumber) ? streetName : $"{streetName}, {streetNumber}");
            AppendSegment(builder, neighborhood);
            AppendSegment(builder, $"{city} - {state}");
            AppendSegment(builder, postalCode);

            return builder.Length == 0 ? fallback : builder.ToString();
        }

        private static void AppendSegment(StringBuilder builder, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(" - ");
            }

            builder.Append(value.Trim());
        }

        private static bool IsSaoPauloAddress(ViaCepResponse viaCep)
            => string.Equals(NormalizeComparable(viaCep.Uf), "SP", StringComparison.Ordinal)
                && string.Equals(NormalizeComparable(viaCep.Localidade), "SAOPAULO", StringComparison.Ordinal);

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

        private static string NormalizeComparable(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark
                    && char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToUpperInvariant(ch));
                }
            }

            return builder.ToString();
        }

        private sealed record ViaCepResponse
        {
            public string? Cep { get; init; }
            public string? Logradouro { get; init; }
            public string? Bairro { get; init; }
            public string? Localidade { get; init; }
            public string? Uf { get; init; }
            public string? Ibge { get; init; }
            public bool Erro { get; init; }
        }
    }
}
