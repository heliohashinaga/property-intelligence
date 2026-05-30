using PropertyIntelligence.Core.Domain;
using Shouldly;

namespace PropertyIntelligence.Tests.Unit;

/// <summary>
/// T040 — TDD red-phase tests for trend calculation logic.
/// Tests the TrendCalculator utility (to be implemented in T042+).
///
/// Trend values: improving | stable | worsening | insufficient_data (< 3 months)
/// </summary>
[Trait("Category", "Unit")]
public sealed class TrendCalculationTests
{
    // ── Linear regression slope (crime trend) ────────────────────────────────

    [Fact]
    public void LinearTrend_RisingPer100kOver24Months_ReturnsWorsening()
    {
        // 24 months of steadily rising crime per 100k → slope > +5%/yr → worsening
        var points = Enumerable.Range(0, 24)
            .Select(i => (double)(50.0 + i * 5.0))   // rises from 50 to 165 (+230%)
            .ToList();

        var trend = TrendCalculator.LinearSlope(points);

        trend.ShouldBe(TrendDirection.Worsening);
    }

    [Fact]
    public void LinearTrend_DecreasingPer100kOver24Months_ReturnsImproving()
    {
        // Steadily decreasing crime → slope < -5%/yr → improving
        var points = Enumerable.Range(0, 24)
            .Select(i => (double)(200.0 - i * 5.0))  // falls from 200 to 85
            .ToList();

        var trend = TrendCalculator.LinearSlope(points);

        trend.ShouldBe(TrendDirection.Improving);
    }

    [Fact]
    public void LinearTrend_FlatPer100kOver24Months_ReturnsStable()
    {
        // Flat crime rate → slope ≈ 0 → stable
        var points = Enumerable.Range(0, 24)
            .Select(_ => 100.0)
            .ToList();

        var trend = TrendCalculator.LinearSlope(points);

        trend.ShouldBe(TrendDirection.Stable);
    }

    [Fact]
    public void LinearTrend_FewerThan3Months_ReturnsInsufficientData()
    {
        // < 3 data points → insufficient_data (cannot compute meaningful trend)
        var points = new List<double> { 100.0, 105.0 };

        var trend = TrendCalculator.LinearSlope(points);

        trend.ShouldBe(TrendDirection.InsufficientData);
    }

    [Fact]
    public void LinearTrend_EmptyPoints_ReturnsInsufficientData()
    {
        var trend = TrendCalculator.LinearSlope([]);

        trend.ShouldBe(TrendDirection.InsufficientData);
    }

    // ── CAGR (appreciation trend) ─────────────────────────────────────────────

    [Fact]
    public void Cagr_PositiveGrowth_ReturnsImproving()
    {
        // valor_venal grew from 300k to 450k over 3 years → positive CAGR → improving
        var history = new List<(int Year, decimal Value)>
        {
            (2021, 300_000m),
            (2022, 360_000m),
            (2023, 420_000m),
            (2024, 450_000m),
        };

        var trend = TrendCalculator.Cagr(history);

        trend.ShouldBe(TrendDirection.Improving);
    }

    [Fact]
    public void Cagr_NegativeGrowth_ReturnsWorsening()
    {
        var history = new List<(int Year, decimal Value)>
        {
            (2021, 500_000m),
            (2022, 460_000m),
            (2023, 430_000m),
            (2024, 400_000m),
        };

        var trend = TrendCalculator.Cagr(history);

        trend.ShouldBe(TrendDirection.Worsening);
    }

    [Fact]
    public void Cagr_NearZeroGrowth_ReturnsStable()
    {
        var history = new List<(int Year, decimal Value)>
        {
            (2022, 400_000m),
            (2023, 402_000m),  // +0.5% — within ±0.5% band
            (2024, 400_500m),
        };

        var trend = TrendCalculator.Cagr(history);

        trend.ShouldBe(TrendDirection.Stable);
    }

    [Fact]
    public void Cagr_FewerThan3Entries_ReturnsInsufficientData()
    {
        var history = new List<(int Year, decimal Value)>
        {
            (2023, 400_000m),
            (2024, 420_000m),
        };

        var trend = TrendCalculator.Cagr(history);

        trend.ShouldBe(TrendDirection.InsufficientData);
    }

    // ── Snapshot delta (mobility / infra) ─────────────────────────────────────

    [Fact]
    public void SnapshotDelta_PositiveDelta_ReturnsImproving()
    {
        // Transit stops increased from 5 to 10 → improving
        var trend = TrendCalculator.SnapshotDelta(prior: 5, current: 10, tolerance: 2);

        trend.ShouldBe(TrendDirection.Improving);
    }

    [Fact]
    public void SnapshotDelta_NegativeDelta_ReturnsWorsening()
    {
        var trend = TrendCalculator.SnapshotDelta(prior: 10, current: 5, tolerance: 2);

        trend.ShouldBe(TrendDirection.Worsening);
    }

    [Fact]
    public void SnapshotDelta_WithinTolerance_ReturnsStable()
    {
        // Delta = 1 stop, tolerance = 2 → stable
        var trend = TrendCalculator.SnapshotDelta(prior: 8, current: 9, tolerance: 2);

        trend.ShouldBe(TrendDirection.Stable);
    }

    [Fact]
    public void SnapshotDelta_NoPriorSnapshot_ReturnsInsufficientData()
    {
        var trend = TrendCalculator.SnapshotDelta(prior: null, current: 8, tolerance: 2);

        trend.ShouldBe(TrendDirection.InsufficientData);
    }
}
