using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NRules;
using NRules.Fluent;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Rules;

/// <summary>
/// Extension method to register the NRules scoring engine in the DI container.
/// Call <c>services.AddPropertyIntelligenceRules()</c> from <c>Program.cs</c>.
/// </summary>
public static class RulesEngineExtensions
{
    public static IServiceCollection AddPropertyIntelligenceRules(
        this IServiceCollection services)
    {
        // Scan this assembly for all Rule subclasses and compile once.
        var repository = new RuleRepository();
        repository.Load(x => x.From(Assembly.GetExecutingAssembly()));
        ISessionFactory factory = repository.Compile();

        // ISessionFactory: singleton — compiled ruleset shared across requests.
        services.AddSingleton(factory);

        // IPropertyAnalysisEngine: scoped — one engine wrapper per request.
        services.AddScoped<IPropertyAnalysisEngine, PropertyAnalysisEngine>();

        return services;
    }
}
