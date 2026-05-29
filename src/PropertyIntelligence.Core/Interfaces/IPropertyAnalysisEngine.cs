using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Core.Interfaces;

/// <summary>
/// Runs the NRules scoring engine over a fully-enriched <see cref="PropertyProfile"/>
/// and produces a <see cref="PropertyAnalysis"/> value object.
/// </summary>
public interface IPropertyAnalysisEngine
{
    /// <summary>
    /// Semver tag of the currently loaded NRules ruleset, e.g. <c>"1.0.0"</c>.
    /// Stored in every <see cref="PropertyAnalysis"/> for traceability (Constitution §II).
    /// </summary>
    string RulesVersion { get; }

    /// <summary>
    /// Scores the property and returns an analysis result.
    /// Pure computation — does NOT persist anything.
    /// </summary>
    PropertyAnalysis Analyze(
        PropertyProfile profile,
        Guid addressId,
        Guid apiConsumerId,
        string? requestIp);
}
