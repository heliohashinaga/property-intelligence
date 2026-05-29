namespace PropertyIntelligence.Core.Domain;

/// <summary>
/// A human-readable warning attached to a property analysis.
/// Stored as JSONB in <c>property_analyses.warnings</c>.
/// All messages are in PT-BR.
/// </summary>
public sealed record AnalysisWarning
{
    /// <summary>
    /// Machine-readable warning code, e.g. "provider_unavailable",
    /// "dimension_capped", "address_ambiguous".
    /// </summary>
    public required string Code { get; init; }

    /// <summary>PT-BR human-readable message for the end user.</summary>
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
            Message   = $"Dados de {providerName} indisponíveis no momento. A dimensão '{dimension}' foi excluída do cálculo.",
            Dimension = dimension
        };

    public static AnalysisWarning InsightUnavailable() =>
        new()
        {
            Code    = "insight_unavailable",
            Message = "Explicação indisponível no momento. O score e as dimensões estão completos.",
        };
}
