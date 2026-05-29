namespace PropertyIntelligence.Core.Domain;

/// <summary>
/// A human-readable warning attached to a property analysis.
/// Stored as JSONB in <c>property_analyses.warnings</c>.
/// All messages are in English.
/// </summary>
public sealed record AnalysisWarning
{
    /// <summary>
    /// Machine-readable warning code, e.g. "provider_unavailable",
    /// "dimension_capped", "address_ambiguous".
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Human-readable message for the end user (English).</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Affected dimension name, e.g. "security".
    /// Null for cross-cutting warnings (e.g. address quality, LLM unavailability).
    /// </summary>
    public string? Dimension { get; init; }

    // ── Factory helpers ──────────────────────────────────────────────────────

    public static AnalysisWarning ProviderUnavailable(string providerName, string dimension) =>
        new()
        {
            Code      = "provider_unavailable",
            Message   = $"Data from '{providerName}' is currently unavailable. The '{dimension}' dimension was excluded from the score.",
            Dimension = dimension
        };

    public static AnalysisWarning InsightUnavailable() =>
        new()
        {
            Code    = "insight_unavailable",
            Message = "Insight generation is temporarily unavailable. The score and all dimensions are complete.",
        };
}
