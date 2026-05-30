using System.Diagnostics.Metrics;

namespace PropertyIntelligence.Api;

/// <summary>
/// Centralized OpenTelemetry metrics for Property Intelligence (T071).
/// Meter name: <c>PropertyIntelligence</c> — must match <c>AddMeter</c> registration in Program.cs.
/// </summary>
public static class AppTelemetry
{
    /// <summary>
    /// The application meter. All instruments share this meter so they are
    /// emitted under the same OTLP resource and aggregated in Grafana.
    /// </summary>
    public static readonly Meter Meter = new("PropertyIntelligence", "1.0");

    /// <summary>End-to-end analysis request duration in milliseconds.</summary>
    public static readonly Histogram<double> AnalysisDuration =
        Meter.CreateHistogram<double>(
            "property_intelligence.analysis.duration_ms",
            unit: "ms",
            description: "End-to-end POST /v1/property/analyze duration");

    /// <summary>Per-provider data fetch duration, tagged by <c>provider</c>.</summary>
    public static readonly Histogram<double> ProviderFetchDuration =
        Meter.CreateHistogram<double>(
            "property_intelligence.provider.fetch_duration_ms",
            unit: "ms",
            description: "Provider FetchAsync duration (tagged: provider)");

    /// <summary>
    /// Number of cache hits across all providers.
    /// Tagged by <c>provider</c> so per-provider hit ratios can be computed.
    /// </summary>
    public static readonly Counter<long> ProviderCacheHit =
        Meter.CreateCounter<long>(
            "property_intelligence.provider.cache_hit_total",
            description: "Provider cache hits (tagged: provider, cached=true|false)");

    /// <summary>Composite score histogram — useful for understanding score distribution by grade.</summary>
    public static readonly Histogram<double> ScoreComposite =
        Meter.CreateHistogram<double>(
            "property_intelligence.score.composite",
            description: "Composite score distribution (tagged: grade)");

    /// <summary>LLM insight generation latency via OpenRouter.</summary>
    public static readonly Histogram<double> InsightGeneration =
        Meter.CreateHistogram<double>(
            "property_intelligence.insight.generation_ms",
            unit: "ms",
            description: "OpenRouter LLM insight generation latency");
}
