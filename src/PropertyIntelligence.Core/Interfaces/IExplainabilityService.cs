using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Core.Interfaces;

/// <summary>
/// Generates a natural-language PT-BR explanation for a property analysis
/// via OpenRouter (Claude / other LLMs configured by <c>LLM_MODEL</c> env var).
/// Best-effort: failures MUST return <c>null</c> — they MUST NOT fail the request.
/// </summary>
public interface IExplainabilityService
{
    /// <summary>
    /// Generates a PT-BR insight paragraph for <paramref name="analysis"/>.
    /// </summary>
    /// <returns>
    /// The generated insight text, or <c>null</c> when the LLM call fails or times out.
    /// Callers must set <c>insight_unavailable = true</c> when null is returned.
    /// </returns>
    Task<string?> GenerateInsightAsync(
        PropertyAnalysis analysis,
        PropertyProfile profile,
        CancellationToken ct = default);
}
