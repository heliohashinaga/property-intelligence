using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Api.Middleware;

/// <summary>
/// Validates the <c>X-Api-Key</c> header on every request (except <c>/health</c>).
/// Looks up the SHA-256 hash of the key in <c>api_consumers</c>; returns 401 on miss.
/// Attaches the resolved <see cref="ApiConsumer"/> and client IP to
/// <see cref="HttpContext.Items"/> for downstream audit use.
/// </summary>
public sealed partial class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;

    public ApiKeyAuthMiddleware(RequestDelegate next, ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // /health is public — no auth required
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var rawKey)
            || string.IsNullOrWhiteSpace(rawKey))
        {
            await WriteUnauthorizedAsync(context, "API key is required");
            return;
        }

        var keyHash = ComputeSha256Hex(rawKey!);

        var db       = context.RequestServices.GetRequiredService<PropertyIntelligenceDbContext>();
        var consumer = await db.ApiConsumers
            .FirstOrDefaultAsync(c => c.ApiKeyHash == keyHash && c.IsActive,
                                 context.RequestAborted);

        if (consumer is null)
        {
            LogInvalidApiKey(_logger, keyHash[..8]);
            await WriteUnauthorizedAsync(context, "Invalid API key");
            return;
        }

        // Attach consumer and originating IP for downstream logging / audit
        context.Items["ApiConsumer"] = consumer;

        var clientIp = context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp)
                       && !string.IsNullOrWhiteSpace(cfIp)
            ? cfIp.ToString()
            : context.Connection.RemoteIpAddress?.ToString();

        context.Items["ClientIp"] = clientIp;

        await _next(context);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode  = 401;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            $"{{\"error\":\"{message}\",\"status\":401}}",
            context.RequestAborted);
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Invalid or inactive API key. HashPrefix={HashPrefix}")]
    private static partial void LogInvalidApiKey(ILogger logger, string hashPrefix);
}
