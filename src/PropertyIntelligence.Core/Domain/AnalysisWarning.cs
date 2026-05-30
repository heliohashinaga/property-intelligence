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

    private static readonly IReadOnlyDictionary<string, string> _dimensionPtBr =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["security"]       = "segurança",
            ["mobility"]       = "mobilidade",
            ["infrastructure"] = "infraestrutura",
            ["environment"]    = "ambiente",
            ["appreciation"]   = "valorização",
            ["urban_context"]  = "contexto urbano",
        };

    /// <summary>T048 — PT-BR warning when a provider is unavailable.</summary>
    public static AnalysisWarning ProviderUnavailable(string providerName, string dimension)
    {
        var dimPtBr = _dimensionPtBr.GetValueOrDefault(dimension, dimension);
        return new()
        {
            Code      = "provider_unavailable",
            Message   = $"A análise de {dimPtBr} não pôde ser realizada por indisponibilidade temporária dos dados ({providerName}).",
            Dimension = dimension,
        };
    }

    public static AnalysisWarning InsightUnavailable() =>
        new()
        {
            Code    = "insight_unavailable",
            Message = "Insight generation is temporarily unavailable. The score and all dimensions are complete.",
        };
}
