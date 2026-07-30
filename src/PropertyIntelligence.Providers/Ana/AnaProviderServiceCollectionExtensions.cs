using Microsoft.Extensions.DependencyInjection;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Ana;

/// <summary>
/// DI registration extension for ANA SNIRH providers.
/// </summary>
public static class AnaProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AnaFloodRiskProvider"/> as a scoped <see cref="IDataProvider{FloodRiskData}"/>.
    /// Cache TTL defaults to 30 days as per task specification.
    /// </summary>
    public static IServiceCollection AddAnaFloodRiskProvider(
        this IServiceCollection services,
        TimeSpan? cacheTtl = null)
    {
        var ttl = cacheTtl ?? TimeSpan.FromDays(30);

        services.AddScoped<IDataProvider<FloodRiskData>>(sp =>
        {
            var dbContext = sp.GetRequiredService<Core.Data.PropertyIntelligenceDbContext>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AnaFloodRiskProvider>>();
            return new AnaFloodRiskProvider(dbContext, logger, ttl);
        });

        return services;
    }
}
