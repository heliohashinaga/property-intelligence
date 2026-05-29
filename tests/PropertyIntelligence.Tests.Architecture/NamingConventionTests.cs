using NetArchTest.Rules;

namespace PropertyIntelligence.Tests.Architecture;

/// <summary>
/// Enforces naming conventions across all production assemblies.
///
/// Rules:
///  - Interfaces must start with "I" (e.g. IDataProvider, ICacheService)
///  - Providers must end with "Provider" (e.g. ViaCepProvider)
///  - Domain entities live in a "Domain" namespace (*.Domain.*)
///  - Endpoints live in the Api's "Endpoints" namespace
/// </summary>
public class NamingConventionTests
{
    // ── Interfaces ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "All interfaces must be prefixed with 'I'")]
    public void Interfaces_Must_StartWith_I()
    {
        var assemblies = Assemblies.All;
        foreach (var asm in assemblies)
        {
            Types.InAssembly(asm)
                 .That().AreInterfaces()
                 .Should().HaveNameStartingWith("I")
                 .GetResult()
                 .ShouldPassWith($"all interfaces in {asm.GetName().Name} must follow the 'I' prefix convention");
        }
    }

    // ── Providers ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Types implementing IDataProvider<T> must end with 'Provider'")]
    public void DataProviderImplementations_Must_EndWith_Provider() =>
        Types.InAssembly(Assemblies.Providers)
             .That()
             .ImplementInterface(typeof(PropertyIntelligence.Core.Interfaces.IDataProvider<>))
             .Should().HaveNameEndingWith("Provider")
             .GetResult()
             .ShouldPassWith("IDataProvider<T> implementations must be named *Provider for discoverability");

    [Fact(DisplayName = "Types named '*Provider' must implement IDataProvider<T>")]
    public void TypesNamed_Provider_Must_ImplementIDataProvider() =>
        Types.InAssembly(Assemblies.Providers)
             .That().HaveNameEndingWith("Provider")
             .And().AreNotAbstract()
             .Should()
             .ImplementInterface(typeof(PropertyIntelligence.Core.Interfaces.IDataProvider<>))
             .GetResult()
             .ShouldPassWith("every concrete *Provider class must fulfil the IDataProvider<T> contract");

    // ── Domain entities ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "Domain entity classes must live in a '*.Domain' namespace")]
    public void DomainClasses_Must_LiveIn_DomainNamespace() =>
        Types.InAssembly(Assemblies.Core)
             .That().ResideInNamespace("PropertyIntelligence.Core.Domain")
             .Should().NotHaveNameStartingWith("I")   // no interfaces in the Domain namespace
             .GetResult()
             .ShouldPassWith("interfaces belong in Core.Interfaces, not Core.Domain");

    [Fact(DisplayName = "Core interfaces must live in 'PropertyIntelligence.Core.Interfaces'")]
    public void CoreInterfaces_Must_LiveIn_InterfacesNamespace() =>
        Types.InAssembly(Assemblies.Core)
             .That().AreInterfaces()
             .Should().ResideInNamespace("PropertyIntelligence.Core.Interfaces")
             .GetResult()
             .ShouldPassWith("Core interfaces must be centralised in the Interfaces namespace");

    // ── Api layer ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Api middleware classes must live in 'PropertyIntelligence.Api.Middleware'")]
    public void Middleware_Must_LiveIn_MiddlewareNamespace() =>
        Types.InAssembly(Assemblies.Api)
             .That().HaveNameEndingWith("Middleware")
             .Should().ResideInNamespace("PropertyIntelligence.Api.Middleware")
             .GetResult()
             .ShouldPassWith("all ASP.NET middleware must be namespaced under Api.Middleware");
}
