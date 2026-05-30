namespace PropertyIntelligence.Rules.Facts;

/// <summary>
/// Emitted by NRules rules. One per scoring tier per dimension.
/// Aggregated by <see cref="PropertyAnalysisEngine"/> after session.Fire().
/// </summary>
public sealed class ScoringFact
{
    /// <summary>Dimension key: "security" | "mobility" | "infrastructure" | "environment" | "appreciation" | "urban_context".</summary>
    public required string Dimension { get; init; }

    /// <summary>Points contributed by this rule (0–200 per rule; engine caps total per dimension at 200).</summary>
    public required int Points { get; init; }

    /// <summary>Human-readable description of the rule that fired.</summary>
    public required string Reason { get; init; }

    /// <summary>Trend direction: "improving" | "stable" | "worsening".</summary>
    public string Trend { get; init; } = "stable";
}
