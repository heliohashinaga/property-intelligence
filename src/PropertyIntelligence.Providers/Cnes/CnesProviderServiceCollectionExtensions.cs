using Microsoft.Extensions.DependencyInjection;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Cnes;

/// <summary>
/// DI registration extension for CNES providers.
/// </summary>
public static class CnesProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CnesHealthProvider"/> as a scoped <see cref="IDataProvider{HealthData}"/>.
    /// Cache TTL defaults to 7 days as per task specification.
    /// </summary>
    public static IServiceCollection AddCnesHealthProvider(
        this IServiceCollection services,
        TimeSpan? cacheTtl = null)
    {
        var ttl = cacheTtl ?? TimeSpan.FromDays(7);

        services.AddScoped<IDataProvider<HealthData>>(sp =>
        {
            var dbContext = sp.GetRequiredService<Core.Data.PropertyIntelligenceDbContext>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CnesHealthProvider>>();
            return new CnesHealthProvider(dbContext, logger, ttl);
        });

        return services;
    }
}
