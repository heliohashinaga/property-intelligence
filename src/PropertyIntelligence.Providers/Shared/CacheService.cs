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
public sealed partial class CacheService : ICacheService
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
            LogRedisGetFailed(_logger, key, ex);
            return default;
        }
        catch (JsonException ex)
        {
            LogDeserializeFailed(_logger, key, ex);
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
            LogRedisSetFailed(_logger, key, ex);
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
            LogRedisDelFailed(_logger, key, ex);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Analysis cache key format: <c>analysis:{SHA256(normalizedAddress.ToLowerInvariant())[..16]}</c>
    /// Only the composed analysis cache is removed; per-provider caches are unaffected.
    /// </remarks>
    public Task InvalidateAnalysisAsync(string normalizedAddress, CancellationToken ct = default)
    {
        var key = AnalysisCacheKey(normalizedAddress);
        return RemoveAsync(key, ct);
    }

    /// <summary>Builds the Redis key for the composed analysis cache.</summary>
    public static string AnalysisCacheKey(string normalizedAddress)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalizedAddress.ToLowerInvariant().Trim()));
        return $"analysis:{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Redis GET failed for key {Key}; treating as cache miss")]
    private static partial void LogRedisGetFailed(ILogger logger, string key, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to deserialize cached value for key {Key}; treating as cache miss")]
    private static partial void LogDeserializeFailed(ILogger logger, string key, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Redis SET failed for key {Key}; cache write skipped")]
    private static partial void LogRedisSetFailed(ILogger logger, string key, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Redis DEL failed for key {Key}")]
    private static partial void LogRedisDelFailed(ILogger logger, string key, Exception ex);
}
