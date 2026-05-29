using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace PropertyIntelligence.Tests.Architecture;

/// <summary>
/// Shared assembly references for all architecture test classes.
/// </summary>
public static class Assemblies
{
    public static readonly Assembly Core          = typeof(PropertyIntelligence.Core.Interfaces.IDataProvider<>).Assembly;
    public static readonly Assembly Providers     = typeof(PropertyIntelligence.Providers.Shared.CacheService).Assembly;
    public static readonly Assembly Rules         = typeof(PropertyIntelligence.Rules.Class1).Assembly;
    public static readonly Assembly Explainability = typeof(PropertyIntelligence.Explainability.Class1).Assembly;
    public static readonly Assembly Api           = typeof(PropertyIntelligence.Api.Middleware.ApiKeyAuthMiddleware).Assembly;

    /// <summary>All production assemblies (AppHost excluded — orchestration only).</summary>
    public static IReadOnlyList<Assembly> All => [Core, Providers, Rules, Explainability, Api];
}

/// <summary>
/// Extension that turns a NetArchTest <see cref="TestResult"/> into an xUnit assertion failure
/// with the list of failing types included in the message.
/// </summary>
public static class TestResultExtensions
{
    public static void ShouldPassWith(this TestResult result, string because = "")
    {
        if (result.IsSuccessful) return;

        var failing = result.FailingTypeNames is { Count: > 0 }
            ? string.Join("\n  - ", result.FailingTypeNames)
            : "(no types listed)";

        var reason = string.IsNullOrWhiteSpace(because) ? "" : $" because {because}";
        result.IsSuccessful.ShouldBeTrue(
            $"Architecture rule violated{reason}.\nFailing types:\n  - {failing}");
    }
}
