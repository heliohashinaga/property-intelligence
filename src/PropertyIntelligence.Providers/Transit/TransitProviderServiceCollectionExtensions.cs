using Microsoft.Extensions.DependencyInjection;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Providers.Transit;

/// <summary>
/// DI registration extension for official São Paulo transit providers.
/// </summary>
public static class TransitProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SpTransGeoSampaTransitProvider"/> as a scoped <see cref="IDataProvider{PoiData}"/>.
    /// Cache TTL defaults to 7 days as per task specification.
    /// </summary>
    public static IServiceCollection AddSpTransGeoSampaTransitProvider(
        this IServiceCollection services,
        TimeSpan? cacheTtl = null)
    {
        var ttl = cacheTtl ?? TimeSpan.FromDays(7);

        services.AddHttpClient("SpTransGeoSampa");

        services.AddScoped<IDataProvider<PoiData>>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient("SpTransGeoSampa");
            var cacheService = sp.GetRequiredService<ICacheService>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SpTransGeoSampaTransitProvider>>();
            return new SpTransGeoSampaTransitProvider(httpClient, ttl, cacheService, logger);
        });

        return services;
    }
}
