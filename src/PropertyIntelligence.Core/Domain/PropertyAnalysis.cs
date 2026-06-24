namespace PropertyIntelligence.Core.Domain;

/// <summary>
/// Append-only audit record for every completed property analysis.
/// MUST NEVER be updated or deleted (Constitution §II — Data Accuracy &amp; Auditability).
/// Persisted in the <c>property_analyses</c> table.
/// </summary>
public sealed record PropertyAnalysis
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid AddressId { get; init; }
    public required Guid ApiConsumerId { get; init; }

    // ── Composite score ──────────────────────────────────────────────────────

    /// <summary>Sum of all available dimension scores (0–1000).</summary>
    public required int CompositeScore { get; init; }

    /// <summary>200 × number of available dimensions; the denominator for percentage display.</summary>
    public required int CompositeMax { get; init; }

    /// <summary>Grade label: A+, A, B+, B, C+, C, D, or F.</summary>
    public required string Grade { get; init; }

    // ── Dimensions ───────────────────────────────────────────────────────────

    /// <summary>One entry per dimension. Stored as JSONB.</summary>
    public required IReadOnlyList<DimensionScore> DimensionScores { get; init; }

    // ── Flags ────────────────────────────────────────────────────────────────

    public required IReadOnlyList<string> RiskFlags { get; init; }
    public required IReadOnlyList<string> OpportunityFlags { get; init; }

    // ── LLM insight ──────────────────────────────────────────────────────────

    /// <summary>PT-BR AI explanation. Null when LLM call failed.</summary>
    public string? Insight { get; init; }
    public bool InsightUnavailable { get; init; }

    // ── Traceability (Constitution §II) ──────────────────────────────────────

    /// <summary>OpenRouter model used, e.g. "anthropic/claude-3-haiku".</summary>
    public required string LlmModel { get; init; }

    /// <summary>NRules ruleset semver tag, e.g. "1.0.0".</summary>
    public required string RulesVersion { get; init; }

    // ── Warnings & provider metadata ─────────────────────────────────────────

    /// <summary>Human-readable PT-BR warnings. Stored as JSONB.</summary>
    public required IReadOnlyList<AnalysisWarning> Warnings { get; init; }
    public required IReadOnlyList<string> ProvidersUsed { get; init; }
    public required IReadOnlyList<string> ProvidersUnavailable { get; init; }

    /// <summary>True when ALL providers were served from Redis cache.</summary>
    public bool Cached { get; init; }

    /// <summary>Requester IP from CF-Connecting-IP header. Nullable.</summary>
    public string? RequestIp { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
