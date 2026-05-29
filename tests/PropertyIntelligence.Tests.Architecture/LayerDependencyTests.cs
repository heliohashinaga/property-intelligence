using NetArchTest.Rules;

namespace PropertyIntelligence.Tests.Architecture;

/// <summary>
/// Enforces the layered dependency rules for the Property Intelligence solution.
///
/// Allowed dependency graph:
///
///   Core  ←  Providers
///   Core  ←  Rules
///   Core  ←  Explainability
///   Core  ←  Api
///         ←  Providers
///         ←  Rules
///         ←  Explainability
///
/// Core must not depend on any other production assembly.
/// Providers/Rules/Explainability must not depend on each other.
/// </summary>
public class LayerDependencyTests
{
    // ── Core is the innermost ring ──────────────────────────────────────────────

    [Fact(DisplayName = "Core must not depend on Providers")]
    public void Core_MustNot_DependOn_Providers() =>
        Types.InAssembly(Assemblies.Core)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Providers")
             .GetResult()
             .ShouldPassWith("Core defines contracts — it must not depend on implementations");

    [Fact(DisplayName = "Core must not depend on Rules")]
    public void Core_MustNot_DependOn_Rules() =>
        Types.InAssembly(Assemblies.Core)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Rules")
             .GetResult()
             .ShouldPassWith("Core must not know about NRules scoring rules");

    [Fact(DisplayName = "Core must not depend on Explainability")]
    public void Core_MustNot_DependOn_Explainability() =>
        Types.InAssembly(Assemblies.Core)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Explainability")
             .GetResult()
             .ShouldPassWith("Core must not depend on LLM explainability layer");

    [Fact(DisplayName = "Core must not depend on Api")]
    public void Core_MustNot_DependOn_Api() =>
        Types.InAssembly(Assemblies.Core)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Api")
             .GetResult()
             .ShouldPassWith("Core must not depend on the presentation/API layer");

    // ── Providers only depend on Core ──────────────────────────────────────────

    [Fact(DisplayName = "Providers must not depend on Rules")]
    public void Providers_MustNot_DependOn_Rules() =>
        Types.InAssembly(Assemblies.Providers)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Rules")
             .GetResult()
             .ShouldPassWith("Providers must remain decoupled from the scoring rules engine");

    [Fact(DisplayName = "Providers must not depend on Explainability")]
    public void Providers_MustNot_DependOn_Explainability() =>
        Types.InAssembly(Assemblies.Providers)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Explainability")
             .GetResult()
             .ShouldPassWith("Providers must not trigger LLM calls");

    [Fact(DisplayName = "Providers must not depend on Api")]
    public void Providers_MustNot_DependOn_Api() =>
        Types.InAssembly(Assemblies.Providers)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Api")
             .GetResult()
             .ShouldPassWith("Providers must not reference the HTTP layer");

    // ── Rules only depend on Core ───────────────────────────────────────────────

    [Fact(DisplayName = "Rules must not depend on Providers")]
    public void Rules_MustNot_DependOn_Providers() =>
        Types.InAssembly(Assemblies.Rules)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Providers")
             .GetResult()
             .ShouldPassWith("Rules must operate only on domain facts, not raw provider data");

    [Fact(DisplayName = "Rules must not depend on Explainability")]
    public void Rules_MustNot_DependOn_Explainability() =>
        Types.InAssembly(Assemblies.Rules)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Explainability")
             .GetResult()
             .ShouldPassWith("Rules must not trigger LLM calls");

    [Fact(DisplayName = "Rules must not depend on Api")]
    public void Rules_MustNot_DependOn_Api() =>
        Types.InAssembly(Assemblies.Rules)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Api")
             .GetResult()
             .ShouldPassWith("Rules must not reference the HTTP layer");

    // ── Explainability only depends on Core ────────────────────────────────────

    [Fact(DisplayName = "Explainability must not depend on Providers")]
    public void Explainability_MustNot_DependOn_Providers() =>
        Types.InAssembly(Assemblies.Explainability)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Providers")
             .GetResult()
             .ShouldPassWith("Explainability generates text from scores, not raw provider data");

    [Fact(DisplayName = "Explainability must not depend on Rules")]
    public void Explainability_MustNot_DependOn_Rules() =>
        Types.InAssembly(Assemblies.Explainability)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Rules")
             .GetResult()
             .ShouldPassWith("Explainability must not depend on the NRules engine");

    [Fact(DisplayName = "Explainability must not depend on Api")]
    public void Explainability_MustNot_DependOn_Api() =>
        Types.InAssembly(Assemblies.Explainability)
             .ShouldNot().HaveDependencyOn("PropertyIntelligence.Api")
             .GetResult()
             .ShouldPassWith("Explainability must not reference the HTTP layer");
}
