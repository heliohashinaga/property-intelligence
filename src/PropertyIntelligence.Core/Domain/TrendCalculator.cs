namespace PropertyIntelligence.Core.Domain;

/// <summary>
/// Pure utility for computing trend direction from time-series data.
/// All methods are allocation-free on the hot path (no LINQ on enumerables that
/// aren't already lists/arrays).
/// </summary>
public static class TrendCalculator
{
    // ── Linear regression slope ────────────────────────────────────────────────
    // Used for: crime rate per 100k over 24 months (T042), transit stops (T079).

    /// <summary>
    /// Computes trend from a sequence of values using ordinary least-squares slope.
    /// </summary>
    /// <param name="values">Ordered time-series values (oldest → newest).</param>
    /// <param name="worsensAbovePctPerYear">
    ///   Annualised slope threshold above which the trend is "worsening".
    ///   Expressed as a percentage of the baseline (first value). Default 5 %.
    /// </param>
    /// <param name="improvesBelow">
    ///   Annualised slope threshold below which the trend is "improving". Default -5 %.
    /// </param>
    /// <returns><see cref="TrendDirection.InsufficientData"/> when fewer than 3 points.</returns>
    public static TrendDirection LinearSlope(
        IReadOnlyList<double> values,
        double worsensAbovePctPerYear = 5.0,
        double improvesBelow = -5.0)
    {
        if (values.Count < 3)
            return TrendDirection.InsufficientData;

        int n = values.Count;

        // OLS: slope = (n·Σxy − Σx·Σy) / (n·Σx² − (Σx)²)
        double sumX = 0, sumY = 0, sumXy = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX  += i;
            sumY  += values[i];
            sumXy += i * values[i];
            sumX2 += (double)i * i;
        }

        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-10)
            return TrendDirection.Stable; // perfectly flat

        double slope = (n * sumXy - sumX * sumY) / denom;

        // Convert slope (units/month) to annualised % change relative to baseline
        double baseline    = values[0] == 0 ? 1.0 : Math.Abs(values[0]);
        double annualisedPct = slope * 12.0 / baseline * 100.0;

        return annualisedPct switch
        {
            var p when p > worsensAbovePctPerYear => TrendDirection.Worsening,
            var p when p < improvesBelow          => TrendDirection.Improving,
            _                                     => TrendDirection.Stable,
        };
    }

    // ── CAGR (compound annual growth rate) ─────────────────────────────────────
    // Used for: IPTU valor_venal appreciation over 36 months (T043).

    /// <summary>
    /// Computes trend from a list of (Year, Value) pairs using CAGR.
    /// </summary>
    /// <param name="history">Historical entries sorted by year (ascending).</param>
    /// <param name="stableThresholdPct">
    ///   CAGR within ±<paramref name="stableThresholdPct"/> % is considered stable. Default 0.5 %.
    /// </param>
    /// <returns><see cref="TrendDirection.InsufficientData"/> when fewer than 3 entries.</returns>
    public static TrendDirection Cagr(
        IReadOnlyList<(int Year, decimal Value)> history,
        double stableThresholdPct = 0.5)
    {
        if (history.Count < 3)
            return TrendDirection.InsufficientData;

        var sorted  = history.OrderBy(h => h.Year).ToList();
        var oldest  = sorted[0];
        var newest  = sorted[^1];
        int years   = newest.Year - oldest.Year;

        if (years <= 0 || oldest.Value <= 0)
            return TrendDirection.InsufficientData;

        double ratio      = (double)(newest.Value / oldest.Value);
        double cagrPct    = (Math.Pow(ratio, 1.0 / years) - 1.0) * 100.0;

        return cagrPct switch
        {
            var c when c > stableThresholdPct  => TrendDirection.Improving,
            var c when c < -stableThresholdPct => TrendDirection.Worsening,
            _                                  => TrendDirection.Stable,
        };
    }

    // ── Snapshot delta ────────────────────────────────────────────────────────
    // Used for: transit stop counts (T079), health/school facility counts (T080),
    //           urban context income group (T081).

    /// <summary>
    /// Computes trend by comparing a current integer count against a prior snapshot.
    /// </summary>
    /// <param name="prior">Prior snapshot value; <c>null</c> when no snapshot exists.</param>
    /// <param name="current">Current measured value.</param>
    /// <param name="tolerance">Delta within this band is considered stable.</param>
    /// <returns><see cref="TrendDirection.InsufficientData"/> when prior is null.</returns>
    public static TrendDirection SnapshotDelta(int? prior, int current, int tolerance = 2)
    {
        if (prior is null)
            return TrendDirection.InsufficientData;

        int delta = current - prior.Value;
        return Math.Abs(delta) <= tolerance
            ? TrendDirection.Stable
            : delta > 0
                ? TrendDirection.Improving
                : TrendDirection.Worsening;
    }
}
