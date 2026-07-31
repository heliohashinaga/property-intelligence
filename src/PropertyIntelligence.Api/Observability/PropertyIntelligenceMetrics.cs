using System.Diagnostics.Metrics;

namespace PropertyIntelligence.Api.Observability;

/// <summary>
/// Custom OpenTelemetry metrics for the Property Intelligence API (T071).
///
/// <para>All instruments are registered on a single <see cref="Meter"/> named
/// <c>PropertyIntelligence</c>, enabling Grafana / OTLP consumers to scope queries
/// with <c>service.meter.name = "PropertyIntelligence"</c>.</para>
///
/// <para>Thread-safe: <see cref="Meter"/> instruments are safe for concurrent use.</para>
/// </summary>
public sealed class PropertyIntelligenceMetrics : IDisposable
{
    /// <summary>OpenTelemetry meter name — used to filter metrics in dashboards.</summary>
    public const string MeterName = "PropertyIntelligence";

    private readonly Meter _meter;

    // ── Analysis ──────────────────────────────────────────────────────────────

    /// <summary>
    /// End-to-end analysis duration in milliseconds.
    /// Records the full wall-clock time from request receipt to response sent,
    /// including enrichment, scoring, and LLM call.
    /// </summary>
    public Histogram<double> AnalysisDurationMs { get; }

    /// <summary>
    /// Composite score (0–1000) distribution.
    /// Useful for understanding the score distribution across analysed addresses.
    /// </summary>
    public Histogram<int> CompositeScore { get; }

    // ── Provider ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-provider fetch duration in milliseconds.
    /// Tagged with <c>provider</c> (e.g. <c>ssp_sp</c>, <c>overpass</c>).
    /// </summary>
    public Histogram<double> ProviderFetchDurationMs { get; }

    /// <summary>
    /// Total number of provider cache hits.
    /// Tagged with <c>provider</c>.
    /// </summary>
    public Counter<long> ProviderCacheHitTotal { get; }

    /// <summary>
    /// Total number of provider cache misses.
    /// Tagged with <c>provider</c>.
    /// </summary>
    public Counter<long> ProviderCacheMissTotal { get; }

    // ── LLM ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Insight generation duration in milliseconds (OpenRouter / LLM call).
    /// </summary>
    public Histogram<double> InsightGenerationMs { get; }

    public PropertyIntelligenceMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        AnalysisDurationMs = _meter.CreateHistogram<double>(
            "property_intelligence.analysis.duration_ms",
            unit: "ms",
            description: "End-to-end analysis duration in milliseconds.");

        CompositeScore = _meter.CreateHistogram<int>(
            "property_intelligence.score.composite",
            description: "Composite property score (0–1000).");

        ProviderFetchDurationMs = _meter.CreateHistogram<double>(
            "property_intelligence.provider.fetch_duration_ms",
            unit: "ms",
            description: "Per-provider fetch duration in milliseconds. Tagged by 'provider'.");

        ProviderCacheHitTotal = _meter.CreateCounter<long>(
            "property_intelligence.provider.cache_hit_total",
            description: "Total provider cache hits. Tagged by 'provider'.");

        ProviderCacheMissTotal = _meter.CreateCounter<long>(
            "property_intelligence.provider.cache_miss_total",
            description: "Total provider cache misses. Tagged by 'provider'.");

        InsightGenerationMs = _meter.CreateHistogram<double>(
            "property_intelligence.insight.generation_ms",
            unit: "ms",
            description: "LLM insight generation duration in milliseconds (OpenRouter call).");
    }

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
