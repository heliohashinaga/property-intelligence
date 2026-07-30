using Microsoft.Extensions.DependencyInjection;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Inep;

/// <summary>
/// DI registration extension for INEP providers.
/// </summary>
public static class InepProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InepSchoolProvider"/> as a scoped <see cref="IDataProvider{SchoolData}"/>.
    /// Cache TTL defaults to 30 days as per task specification.
    /// </summary>
    public static IServiceCollection AddInepSchoolProvider(
        this IServiceCollection services,
        TimeSpan? cacheTtl = null)
    {
        var ttl = cacheTtl ?? TimeSpan.FromDays(30);

        services.AddScoped<IDataProvider<SchoolData>>(sp =>
        {
            var dbContext = sp.GetRequiredService<Core.Data.PropertyIntelligenceDbContext>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InepSchoolProvider>>();
            return new InepSchoolProvider(dbContext, logger, ttl);
        });

        return services;
    }
}
