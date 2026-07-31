using FluentAssertions;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Services;

namespace PropertyIntelligence.Tests.Unit;

/// <summary>
/// T040 — unit tests for trend calculation logic in TrendCalculator.
/// Written test-first; tests for CrimeDataProvider end-to-end integration
/// are exercised via the SecurityTrend field once T042 is implemented.
/// </summary>
public sealed class TrendCalculationTests
{
    // ── TrendCalculator.ComputeTrend ─────────────────────────────────────────

    [Fact]
    public void ComputeTrend_with_rising_24_months_returns_worsening()
    {
        // Crime rate rising from 100 to 200 over 24 months — clearly worsening
        var dataPoints = Enumerable.Range(0, 24)
            .Select(i => (MonthIndex: i, Per100k: 100.0 + i * (100.0 / 23)))
            .ToList();

        var result = TrendCalculator.ComputeTrend(dataPoints);

        result.Should().Be(TrendDirection.Worsening);
    }

    [Fact]
    public void ComputeTrend_with_flat_24_months_returns_stable()
    {
        // Crime rate perfectly flat at 150 per 100k
        var dataPoints = Enumerable.Range(0, 24)
            .Select(i => (MonthIndex: i, Per100k: 150.0))
            .ToList();

        var result = TrendCalculator.ComputeTrend(dataPoints);

        result.Should().Be(TrendDirection.Stable);
    }

    [Fact]
    public void ComputeTrend_with_decreasing_24_months_returns_improving()
    {
        // Crime rate falling from 200 to 100 over 24 months — clearly improving
        var dataPoints = Enumerable.Range(0, 24)
            .Select(i => (MonthIndex: i, Per100k: 200.0 - i * (100.0 / 23)))
            .ToList();

        var result = TrendCalculator.ComputeTrend(dataPoints);

        result.Should().Be(TrendDirection.Improving);
    }

    [Fact]
    public void ComputeTrend_with_small_noise_below_threshold_returns_stable()
    {
        // Crime rate at 100 with very slight upward noise — within 5% threshold
        // Annual change ≈ 2% of 100 = 2, which is < 5 threshold
        var dataPoints = Enumerable.Range(0, 24)
            .Select(i => (MonthIndex: i, Per100k: 100.0 + i * (2.0 / 23)))
            .ToList();

        var result = TrendCalculator.ComputeTrend(dataPoints);

        result.Should().Be(TrendDirection.Stable);
    }

    [Fact]
    public void ComputeTrend_with_single_data_point_returns_stable()
    {
        var dataPoints = new List<(int, double)> { (0, 120.0) };

        var result = TrendCalculator.ComputeTrend(dataPoints);

        result.Should().Be(TrendDirection.Stable);
    }

    [Fact]
    public void ComputeTrend_with_empty_data_returns_stable()
    {
        var result = TrendCalculator.ComputeTrend([]);

        result.Should().Be(TrendDirection.Stable);
    }

    [Fact]
    public void ComputeTrend_with_zero_mean_returns_stable()
    {
        // All zeros — percentage change undefined, should return stable
        var dataPoints = Enumerable.Range(0, 24)
            .Select(i => (MonthIndex: i, Per100k: 0.0))
            .ToList();

        var result = TrendCalculator.ComputeTrend(dataPoints);

        result.Should().Be(TrendDirection.Stable);
    }

    [Fact]
    public void ComputeTrend_just_below_five_percent_threshold_returns_stable()
    {
        // Mean ≈ 100, annual change = 4.9% → just below 5% threshold → stable
        var baseline = 100.0;
        var annualChange = baseline * 0.049; // 4.9% — clearly below 5% threshold
        var monthlySlope = annualChange / 12.0;
        var meanIndex = 11.5; // actual mean of indices 0..23
        var startValue = baseline - monthlySlope * meanIndex;
        var dataPoints = Enumerable.Range(0, 24)
            .Select(i => (MonthIndex: i, Per100k: startValue + monthlySlope * i))
            .ToList();

        var result = TrendCalculator.ComputeTrend(dataPoints);

        result.Should().Be(TrendDirection.Stable);
    }

    [Fact]
    public void ComputeTrend_just_above_five_percent_threshold_returns_worsening()
    {
        // Mean = 100, annual change = 5.1 → just above 5% threshold → worsening
        var baseline = 100.0;
        var annualChange = baseline * 0.051; // 5.1% → strictly above threshold
        var monthlySlope = annualChange / 12.0;
        var midpoint = 12.0;
        var startValue = baseline - monthlySlope * midpoint;
        var dataPoints = Enumerable.Range(0, 24)
            .Select(i => (MonthIndex: i, Per100k: startValue + monthlySlope * i))
            .ToList();

        var result = TrendCalculator.ComputeTrend(dataPoints);

        result.Should().Be(TrendDirection.Worsening);
    }

    [Fact]
    public void ComputeTrend_just_below_negative_five_percent_threshold_returns_improving()
    {
        // Mean = 100, annual change = -5.1 → just below -5% threshold → improving
        var baseline = 100.0;
        var annualChange = -(baseline * 0.051);
        var monthlySlope = annualChange / 12.0;
        var midpoint = 12.0;
        var startValue = baseline - monthlySlope * midpoint;
        var dataPoints = Enumerable.Range(0, 24)
            .Select(i => (MonthIndex: i, Per100k: startValue + monthlySlope * i))
            .ToList();

        var result = TrendCalculator.ComputeTrend(dataPoints);

        result.Should().Be(TrendDirection.Improving);
    }
}
