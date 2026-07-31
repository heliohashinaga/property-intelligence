using Microsoft.Extensions.DependencyInjection;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Ibge;

/// <summary>
/// DI registration extension for IBGE providers.
/// </summary>
public static class IbgeProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IbgeCensusProvider"/> as a scoped <see cref="IDataProvider{CensusData}"/>.
    /// Cache TTL defaults to 30 days as per task specification.
    /// </summary>
    public static IServiceCollection AddIbgeCensusProvider(
        this IServiceCollection services,
        TimeSpan? cacheTtl = null)
    {
        var ttl = cacheTtl ?? TimeSpan.FromDays(30);

        services.AddScoped<IDataProvider<CensusData>>(sp =>
        {
            var dbContext = sp.GetRequiredService<Core.Data.PropertyIntelligenceDbContext>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<IbgeCensusProvider>>();
            var cacheService = sp.GetRequiredService<ICacheService>();
            return new IbgeCensusProvider(dbContext, logger, ttl, cacheService);
        });

        return services;
    }
}
