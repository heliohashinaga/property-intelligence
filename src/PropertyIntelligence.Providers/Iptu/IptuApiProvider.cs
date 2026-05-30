using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;
using PropertyIntelligence.Providers.Shared;

namespace PropertyIntelligence.Providers.Iptu;

/// <summary>
/// Fetches IPTU (property tax) assessment data from iptuapi.com.br.
/// Returns <c>null</c> gracefully on 404 or error — IPTU data is best-effort.
/// Implements <see cref="IDataProvider{IptuData}"/>.
/// </summary>
public sealed class IptuApiProvider : IDataProvider<IptuData?>
{
    public string   ProviderName => "iptu_api";
    public TimeSpan CacheTtl     => TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory       _http;
    private readonly ICacheService            _cache;
    private readonly IConfiguration           _config;
    private readonly ILogger<IptuApiProvider> _logger;

    public IptuApiProvider(
        IHttpClientFactory       http,
        ICacheService            cache,
        IConfiguration           config,
        ILogger<IptuApiProvider> logger)
    {
        _http   = http;
        _cache  = cache;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IptuData?> FetchAsync(PropertyAddress address, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyHelper.BuildKey(ProviderName, address.NormalizedAddress);
        var cached   = await _cache.GetAsync<IptuData>(cacheKey, ct).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var result = await FetchFromApiAsync(address, ct).ConfigureAwait(false);
        if (result is not null)
            await _cache.SetAsync(cacheKey, result, CacheTtl, ct).ConfigureAwait(false);

        return result;
    }

    // ── private ──────────────────────────────────────────────────────────────

    private async Task<IptuData?> FetchFromApiAsync(PropertyAddress address, CancellationToken ct)
    {
        try
        {
            var apiKey  = _config["IPTU_API_KEY"] ?? string.Empty;
            var query   = BuildQuery(address);
            var client  = _http.CreateClient("iptu_api");
            var url     = $"v1/property?address={Uri.EscapeDataString(query)}&api_key={Uri.EscapeDataString(apiKey)}";
            var response = await client.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("IptuApiProvider: no record for '{Address}'", address.NormalizedAddress);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var dto = await response.Content
                .ReadFromJsonAsync<IptuApiResponse>(_json, ct)
                .ConfigureAwait(false);

            if (dto is null) return null;

            return new IptuData
            {
                ValorVenal = dto.ValorVenal,
                ZoningClass = dto.Zoneamento,
                // AppreciationTrend populated in US2 (Phase 4)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "IptuApiProvider: error fetching IPTU data for '{Address}'",
                address.NormalizedAddress);
            return null;
        }
    }

    private static string BuildQuery(PropertyAddress address)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(address.StreetName))   parts.Add(address.StreetName);
        if (!string.IsNullOrWhiteSpace(address.StreetNumber)) parts.Add(address.StreetNumber);
        if (!string.IsNullOrWhiteSpace(address.City))         parts.Add(address.City);
        return string.Join(" ", parts);
    }
}
