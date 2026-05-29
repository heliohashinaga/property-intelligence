using NetArchTest.Rules;

namespace PropertyIntelligence.Tests.Architecture;

/// <summary>
/// Enforces observability requirements from the project constitution:
///
///  "Every HTTP handler must emit a structured log entry with correlation_id,
///   operation, duration_ms, and outcome."
///
/// Rules:
///  - All middleware classes must depend on ILogger (direct injection)
///  - No classes outside Api.Middleware may inject IHttpContextAccessor
///    (correlation ID extraction belongs in middleware, not business logic)
/// </summary>
public class ObservabilityTests
{
    [Fact(DisplayName = "Api middleware must depend on Microsoft.Extensions.Logging")]
    public void Middleware_Must_Depend_On_Logging() =>
        Types.InAssembly(Assemblies.Api)
             .That().HaveNameEndingWith("Middleware")
             .Should().HaveDependencyOn("Microsoft.Extensions.Logging")
             .GetResult()
             .ShouldPassWith("all middleware must log via ILogger for structured observability");

    [Fact(DisplayName = "Domain classes in Core must not depend on logging abstractions")]
    public void DomainClasses_MustNot_DependOn_Logging() =>
        Types.InAssembly(Assemblies.Core)
             .That().ResideInNamespace("PropertyIntelligence.Core.Domain")
             .ShouldNot().HaveDependencyOn("Microsoft.Extensions.Logging")
             .GetResult()
             .ShouldPassWith("domain entities are pure value objects — no infrastructure dependencies");

    [Fact(DisplayName = "Domain classes in Core must not depend on Microsoft.EntityFrameworkCore directly")]
    public void DomainClasses_MustNot_DependOn_EFCore()
    {
        // EF Core attributes are allowed in entity classes (annotations), but direct
        // DbContext / DbSet references must stay in the Data layer, not in Domain classes.
        Types.InAssembly(Assemblies.Core)
             .That().ResideInNamespace("PropertyIntelligence.Core.Domain")
             .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore.DbContext")
             .GetResult()
             .ShouldPassWith("domain entities must not hold direct DbContext references (use repositories or the DbContext from Core.Data)");
    }
}
