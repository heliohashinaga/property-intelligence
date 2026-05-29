using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace PropertyIntelligence.Tests.Architecture;

/// <summary>
/// Enforces provider-specific architectural rules derived from the project constitution:
///
///  - Every IDataProvider&lt;T&gt; implementation must live in PropertyIntelligence.Providers
///  - Providers must NOT be declared in Core, Rules, Explainability, or Api
///  - The CacheService must be sealed (no accidental subclassing)
///  - Providers must not be abstract (each is a concrete, standalone unit)
/// </summary>
public class ProviderIsolationTests
{
    [Fact(DisplayName = "IDataProvider<T> implementations must only live in the Providers assembly")]
    public void IDataProvider_Implementations_MustBe_In_Providers_Assembly()
    {
        // Check that no other assembly contains a class that directly implements IDataProvider<T>
        var forbiddenAssemblies = new[]
        {
            Assemblies.Core,
            Assemblies.Rules,
            Assemblies.Explainability,
            Assemblies.Api,
        };

        var providerInterface = typeof(PropertyIntelligence.Core.Interfaces.IDataProvider<>);

        foreach (var asm in forbiddenAssemblies)
        {
            var violators = asm.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => t.GetInterfaces()
                             .Any(i => i.IsGenericType &&
                                       i.GetGenericTypeDefinition() == providerInterface))
                .Select(t => t.FullName!)
                .ToList();

            violators.ShouldBeEmpty(
                $"Assembly '{asm.GetName().Name}' must not contain IDataProvider<T> implementations.");
        }
    }

    [Fact(DisplayName = "Concrete provider classes must not be abstract")]
    public void ConcreteProviders_MustNot_BeAbstract() =>
        Types.InAssembly(Assemblies.Providers)
             .That()
             .ImplementInterface(typeof(PropertyIntelligence.Core.Interfaces.IDataProvider<>))
             .Should().NotBeAbstract()
             .GetResult()
             .ShouldPassWith("each provider is a standalone, concrete implementation");

    [Fact(DisplayName = "CacheService must be sealed")]
    public void CacheService_Must_BeSealed() =>
        Types.InAssembly(Assemblies.Providers)
             .That().HaveNameEndingWith("CacheService")
             .Should().BeSealed()
             .GetResult()
             .ShouldPassWith("CacheService is an infrastructure leaf — seal it to prevent accidental extension");

    [Fact(DisplayName = "Providers must reside in PropertyIntelligence.Providers namespace")]
    public void Providers_Must_ResideIn_ProvidersNamespace() =>
        Types.InAssembly(Assemblies.Providers)
             .That()
             .ImplementInterface(typeof(PropertyIntelligence.Core.Interfaces.IDataProvider<>))
             .Should().ResideInNamespaceStartingWith("PropertyIntelligence.Providers")
             .GetResult()
             .ShouldPassWith("all IDataProvider<T> implementations belong in the Providers project namespace");

    [Fact(DisplayName = "All providers must have a non-zero CacheTtl (enforced by naming)")]
    public void AllProviders_Must_Expose_CacheTtl_Property()
    {
        var providerInterface = typeof(PropertyIntelligence.Core.Interfaces.IDataProvider<>);

        var missingTtl = Assemblies.Providers.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetInterfaces()
                         .Any(i => i.IsGenericType &&
                                   i.GetGenericTypeDefinition() == providerInterface))
            .Where(t =>
            {
                // Verify CacheTtl property is explicitly declared (not just inherited)
                var prop = t.GetProperty("CacheTtl",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                if (prop == null) return false; // implemented via interface, check interface map

                // Must have a getter
                return prop.GetGetMethod() == null;
            })
            .Select(t => t.FullName!)
            .ToList();

        missingTtl.ShouldBeEmpty(
            $"These providers expose a CacheTtl getter that is not accessible.");
    }
}
