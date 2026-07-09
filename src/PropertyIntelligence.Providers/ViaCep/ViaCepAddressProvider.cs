using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.ViaCep
{
    /// <summary>
    /// Skeleton implementation of a ViaCEP address provider.
    /// This minimal stub satisfies the compile-time contract and the Test-First
    /// reflection test (T089). Full implementation (HTTP call, parsing and
    /// municipality/state validation) is T092.
    /// </summary>
    public sealed class ViaCepAddressProvider : IDataProvider<AddressInfo>
    {
        private readonly HttpClient _httpClient;
        private readonly TimeSpan _cacheTtl;

        public ViaCepAddressProvider(HttpClient httpClient, TimeSpan cacheTtl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _cacheTtl = cacheTtl;
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
                var fallback = new AddressInfo
                {
                    NormalizedAddress = address.NormalizedAddress,
                    StreetName = address.StreetName,
                    StreetNumber = address.StreetNumber,
                    Neighborhood = address.Neighborhood,
                    City = address.City,
                    State = address.State,
                    PostalCode = address.PostalCode,
                    Lat = address.Lat,
                    Lng = address.Lng,
                };

                return new ProviderFetchResult<AddressInfo>
                {
                    Data = fallback,
                    RawPayload = "{}",
                };
            }

            // Normalize CEP to 8 digits (remove non-digits)
            var cep = System.Text.RegularExpressions.Regex.Replace(address.PostalCode, "\\D", string.Empty);
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

            // Parse JSON and detect ViaCEP 'erro' result
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("erro", out var erro) && erro.ValueKind == JsonValueKind.True)
                {
                    throw new InvalidOperationException($"CEP {cep} not found in ViaCEP");
                }

                var logradouro = root.TryGetProperty("logradouro", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
                var complemento = root.TryGetProperty("complemento", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                var bairro = root.TryGetProperty("bairro", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
                var localidade = root.TryGetProperty("localidade", out var loc) && loc.ValueKind == JsonValueKind.String ? loc.GetString() : null;
                var uf = root.TryGetProperty("uf", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                var returnedCep = root.TryGetProperty("cep", out var cp) && cp.ValueKind == JsonValueKind.String ? cp.GetString() : null;

                var normalized = address.NormalizedAddress;
                if (!string.IsNullOrWhiteSpace(logradouro) && !string.IsNullOrWhiteSpace(localidade) && !string.IsNullOrWhiteSpace(uf))
                {
                    // Build a reasonable normalized address when possible
                    normalized = $"{logradouro}{(string.IsNullOrWhiteSpace(address.StreetNumber) ? string.Empty : ", " + address.StreetNumber)} - {bairro ?? string.Empty}, {localidade} - {uf}";
                }

                var info = new AddressInfo
                {
                    NormalizedAddress = normalized,
                    StreetName = logradouro ?? address.StreetName,
                    StreetNumber = address.StreetNumber,
                    Neighborhood = bairro ?? address.Neighborhood,
                    City = localidade ?? address.City,
                    State = uf ?? address.State,
                    PostalCode = returnedCep ?? address.PostalCode,
                    Lat = address.Lat,
                    Lng = address.Lng,
                };

                return new ProviderFetchResult<AddressInfo>
                {
                    Data = info,
                    RawPayload = body,
                };
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Failed to parse ViaCEP response", ex);
            }
        }
    }
}
