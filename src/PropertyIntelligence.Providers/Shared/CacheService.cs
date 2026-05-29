using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Interfaces;
using StackExchange.Redis;

namespace PropertyIntelligence.Providers.Shared;

/// <summary>
/// Redis-backed implementation of <see cref="ICacheService"/>.
/// All Redis errors are caught, logged, and swallowed — callers receive a cache miss
/// rather than an exception, keeping the system resilient when Redis is unavailable.
/// </summary>
public sealed class CacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly ILogger<CacheService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public CacheService(IConnectionMultiplexer redis, ILogger<CacheService> logger)
    {
        _db     = redis.GetDatabase();
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _db.StringGetAsync(key).ConfigureAwait(false);

            if (!value.HasValue)
                return default;

            return JsonSerializer.Deserialize<T>((string)value!, _jsonOptions);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for key {Key}; treating as cache miss", key);
            return default;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cached value for key {Key}; treating as cache miss", key);
            return default;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            await _db.StringSetAsync(key, json, ttl).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for key {Key}; cache write skipped", key);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _db.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis DEL failed for key {Key}", key);
        }
    }
}
