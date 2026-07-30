using Microsoft.Extensions.DependencyInjection;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Crime;

/// <summary>
/// DI registration extension for Crime data providers.
/// </summary>
public static class CrimeProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CrimeDataProvider"/> as a scoped <see cref="IDataProvider{CrimeData}"/>.
    /// Cache TTL defaults to 24 hours as per task specification.
    /// </summary>
    public static IServiceCollection AddCrimeDataProvider(
        this IServiceCollection services,
        TimeSpan? cacheTtl = null)
    {
        var ttl = cacheTtl ?? TimeSpan.FromHours(24);

        services.AddScoped<IDataProvider<CrimeData>>(sp =>
        {
            var dbContext = sp.GetRequiredService<Core.Data.PropertyIntelligenceDbContext>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CrimeDataProvider>>();
            return new CrimeDataProvider(dbContext, logger, ttl);
        });

        return services;
    }
}
