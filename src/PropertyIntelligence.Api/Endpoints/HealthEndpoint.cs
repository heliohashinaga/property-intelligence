using Microsoft.EntityFrameworkCore;
using PropertyIntelligence.Core.Data;
using StackExchange.Redis;

namespace PropertyIntelligence.Api.Endpoints;

/// <summary>
/// Maps <c>GET /health</c>.
/// Returns 200 (healthy or degraded) or 503 (database unreachable).
/// Does NOT require an API key — publicly accessible for load-balancer checks.
/// Response shape matches <c>contracts/health-endpoint.md</c>.
/// </summary>
public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (HttpContext ctx) =>
        {
            var db    = ctx.RequestServices.GetRequiredService<PropertyIntelligenceDbContext>();
            var redis = ctx.RequestServices.GetService<IConnectionMultiplexer>();

            var (dbStatus, dbHealthy)       = await CheckDatabaseAsync(db, ctx.RequestAborted);
            var (redisStatus, redisHealthy) = await CheckRedisAsync(redis, ctx.RequestAborted);

            var overallStatus = (dbHealthy && redisHealthy) ? "healthy" : "degraded";
            var httpStatus    = dbHealthy ? 200 : 503;

            var response = new
            {
                status    = overallStatus,
                version   = "1.0.0",
                checks    = new { database = dbStatus, redis = redisStatus },
                timestamp = DateTimeOffset.UtcNow,
            };

            return Results.Json(response, statusCode: httpStatus);
        })
        .WithName("Health")
        .ExcludeFromDescription(); // keep out of any future OpenAPI docs

        return app;
    }

    // ── Dependency checks ─────────────────────────────────────────────────────

    private static async Task<(string status, bool healthy)> CheckDatabaseAsync(
        PropertyIntelligenceDbContext db,
        CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return ("healthy", true);
        }
        catch (Exception)
        {
            return ("unhealthy", false);
        }
    }

    private static async Task<(string status, bool healthy)> CheckRedisAsync(
        IConnectionMultiplexer? redis,
        CancellationToken ct)
    {
        if (redis is null)
            return ("not_configured", false);

        try
        {
            await redis.GetDatabase().PingAsync();
            return ("healthy", true);
        }
        catch (Exception)
        {
            return ("unhealthy", false);
        }
    }
}
