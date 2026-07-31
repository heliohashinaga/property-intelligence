using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Core.Services;

/// <summary>
/// Pure static utility for computing trend direction from a time-series of
/// per-100k rate values using ordinary least-squares linear regression.
///
/// <para>Thresholds (annualised slope as a fraction of the baseline mean):</para>
/// <list type="bullet">
///   <item>slope × 12 &gt; +5 % of mean → <see cref="TrendDirection.Worsening"/></item>
///   <item>slope × 12 &lt; −5 % of mean → <see cref="TrendDirection.Improving"/></item>
///   <item>otherwise                    → <see cref="TrendDirection.Stable"/></item>
/// </list>
/// </summary>
public static class TrendCalculator
{
    /// <summary>Annual rate change threshold (5 % of baseline mean) that separates stable from trending.</summary>
    private const double ThresholdFraction = 0.05;

    /// <summary>
    /// Computes the trend direction from an ordered list of <c>(monthIndex, per100k)</c> pairs.
    ///
    /// <para><paramref name="dataPoints"/> must be ordered chronologically;
    /// month index 0 = oldest, n−1 = most recent.</para>
    /// <para>Returns <see cref="TrendDirection.Stable"/> when fewer than 2 data points
    /// are supplied or when the mean rate is zero (undefined percentage change).</para>
    /// </summary>
    public static TrendDirection ComputeTrend(IReadOnlyList<(int MonthIndex, double Per100k)> dataPoints)
    {
        if (dataPoints.Count < 2)
        {
            return TrendDirection.Stable;
        }

        var n = dataPoints.Count;
        double sumX = 0, sumY = 0, sumXy = 0, sumX2 = 0;

        foreach (var (monthIndex, per100k) in dataPoints)
        {
            sumX += monthIndex;
            sumY += per100k;
            sumXy += monthIndex * per100k;
            sumX2 += (double)monthIndex * monthIndex;
        }

        var meanX = sumX / n;
        var meanY = sumY / n;

        var denominator = sumX2 - n * meanX * meanX;
        if (Math.Abs(denominator) < double.Epsilon || meanY <= 0)
        {
            return TrendDirection.Stable;
        }

        // Monthly OLS slope (units: rate-per-100k / month)
        var slope = (sumXy - n * meanX * meanY) / denominator;

        // Annualise: multiply by 12 to get rate change per year
        var annualChange = slope * 12;
        var threshold = meanY * ThresholdFraction;

        if (annualChange > threshold)
        {
            return TrendDirection.Worsening;
        }

        if (annualChange < -threshold)
        {
            return TrendDirection.Improving;
        }

        return TrendDirection.Stable;
    }
}
