using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Shared;

namespace PropertyIntelligence.Providers.ViaCep;

/// <summary>
/// Fetches structured address data from ViaCEP and resolves lat/lng via Nominatim.
/// Implements <see cref="IDataProvider{PropertyAddress}"/>.
/// </summary>
public sealed class ViaCepProvider : IDataProvider<PropertyAddress>
{
    public string   ProviderName => "viacep";
    public TimeSpan CacheTtl     => TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _http;
    private readonly ICacheService      _cache;
    private readonly ILogger<ViaCepProvider> _logger;

    public ViaCepProvider(
        IHttpClientFactory      http,
        ICacheService           cache,
        ILogger<ViaCepProvider> logger)
    {
        _http   = http;
        _cache  = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PropertyAddress> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var cep = ExtractCep(address);
        if (string.IsNullOrEmpty(cep))
            throw new ProviderException(ProviderName, $"Cannot extract CEP from address '{address.NormalizedAddress}'");

        var cacheKey = CacheKeyHelper.BuildKey(ProviderName, cep);
        var cached   = await _cache.GetAsync<PropertyAddress>(cacheKey, ct).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var result = await FetchFromViaCepAsync(cep, ct).ConfigureAwait(false);
        await _cache.SetAsync(cacheKey, result, CacheTtl, ct).ConfigureAwait(false);
        return result;
    }

    // ── private ──────────────────────────────────────────────────────────────

    private async Task<PropertyAddress> FetchFromViaCepAsync(string cep, CancellationToken ct)
    {
        var client = _http.CreateClient("viacep");
        var response = await client.GetAsync($"ws/{cep}/json/", ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new ProviderException(ProviderName, $"CEP not found: {cep}");

        response.EnsureSuccessStatusCode();

        var dto = await response.Content
            .ReadFromJsonAsync<ViaCepResponse>(_json, ct)
            .ConfigureAwait(false);

        if (dto is null || dto.Erro == "true")
            throw new ProviderException(ProviderName, $"CEP not found: {cep}");

        var cleanCep = dto.Cep?.Replace("-", "") ?? cep;

        // Resolve coordinates via Nominatim (best-effort)
        double? lat = null, lng = null;
        try
        {
            (lat, lng) = await ResolveCoordinatesAsync(dto, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nominatim geocoding failed for CEP {Cep}; coordinates will be null", cep);
        }

        var normalized = BuildNormalized(dto);

        return new PropertyAddress
        {
            NormalizedAddress = normalized,
            StreetName        = dto.Logradouro,
            Neighborhood      = dto.Bairro,
            City              = dto.Localidade ?? string.Empty,
            State             = dto.Uf         ?? string.Empty,
            PostalCode        = cleanCep,
            Lat               = lat,
            Lng               = lng,
        };
    }

    private async Task<(double? lat, double? lng)> ResolveCoordinatesAsync(
        ViaCepResponse dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Localidade))
            return (null, null);

        var query = Uri.EscapeDataString(
            $"{dto.Logradouro} {dto.Localidade} {dto.Uf} Brasil".Trim());

        var client   = _http.CreateClient("nominatim");
        var url      = $"search?q={query}&format=json&countrycodes=br&limit=1";
        var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        var root = doc.RootElement;
        if (root.GetArrayLength() == 0)
            return (null, null);

        var first = root[0];
        if (first.TryGetProperty("lat", out var latEl) &&
            first.TryGetProperty("lon", out var lngEl) &&
            double.TryParse(latEl.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(lngEl.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lng))
        {
            return (lat, lng);
        }

        return (null, null);
    }

    private static string BuildNormalized(ViaCepResponse dto)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(dto.Logradouro)) parts.Add(dto.Logradouro);
        if (!string.IsNullOrWhiteSpace(dto.Bairro))     parts.Add(dto.Bairro);
        if (!string.IsNullOrWhiteSpace(dto.Localidade)) parts.Add(dto.Localidade);
        if (!string.IsNullOrWhiteSpace(dto.Uf))         parts.Add(dto.Uf);
        if (!string.IsNullOrWhiteSpace(dto.Cep))        parts.Add(dto.Cep);
        return string.Join(", ", parts);
    }

    private static string? ExtractCep(PropertyAddress address)
    {
        // Prefer explicit PostalCode field
        if (!string.IsNullOrWhiteSpace(address.PostalCode))
            return Regex.Replace(address.PostalCode, @"\D", "")[..Math.Min(8, address.PostalCode.Length)];

        // Try to find a CEP pattern in the normalized address
        var match = Regex.Match(address.NormalizedAddress, @"\b(\d{5})-?(\d{3})\b");
        if (match.Success)
            return match.Groups[1].Value + match.Groups[2].Value;

        return null;
    }
}
