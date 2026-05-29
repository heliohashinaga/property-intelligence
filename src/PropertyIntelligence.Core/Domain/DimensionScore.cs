namespace PropertyIntelligence.Core.Domain;

/// <summary>Availability status of a scoring dimension.</summary>
public enum DimensionStatus
{
    Available,
    Unavailable
}

/// <summary>Trend direction over the historical window for a dimension.</summary>
public enum TrendDirection
{
    Improving,
    Stable,
    Worsening
}

/// <summary>
/// Score for a single analysis dimension.
/// Stored as JSONB in <c>property_analyses.dimension_scores</c>.
/// </summary>
public sealed record DimensionScore
{
    /// <summary>
    /// Dimension identifier: "security" | "mobility" | "infrastructure" |
    /// "environment" | "appreciation" | "urban_context".
    /// </summary>
    public required string Dimension { get; init; }

    /// <summary>Score 0–200. Null when <see cref="Status"/> is <see cref="DimensionStatus.Unavailable"/>.</summary>
    public int? Score { get; init; }

    /// <summary>Always 200 — the maximum for any single dimension.</summary>
    public int Max { get; init; } = 200;

    /// <summary>Trend direction. Null when <see cref="Status"/> is <see cref="DimensionStatus.Unavailable"/>.</summary>
    public TrendDirection? Trend { get; init; }

    /// <summary>Whether this dimension has a live score or was skipped due to provider unavailability.</summary>
    public required DimensionStatus Status { get; init; }

    // ── Factory helpers ──────────────────────────────────────────────────────

    public static DimensionScore Available(string dimension, int score, TrendDirection trend) =>
        new() { Dimension = dimension, Score = score, Trend = trend, Status = DimensionStatus.Available };

    public static DimensionScore Unavailable(string dimension) =>
        new() { Dimension = dimension, Score = null, Trend = null, Status = DimensionStatus.Unavailable };
}
