namespace Gloria.Ml;

/// <summary>Rows whose feature value fell in [Lower, Upper), with the share that had the label set.</summary>
public sealed record FeatureBin(float Lower, float Upper, int Count, int Positives)
{
    public double Rate => Count > 0 ? Positives / (double)Count : double.NaN;

    /// <summary>95% Wilson score interval for <see cref="Rate"/>.</summary>
    public (double Low, double High) Interval => FeatureBinReport.Wilson(Positives, Count);
}

/// <summary>The cut that best separates the label rate below and above it, with a chi-square statistic.</summary>
public sealed record ThresholdScan(float Threshold, int Below, double RateBelow, int Above, double RateAbove, double ChiSquare);

/// <summary>Binned label rates per feature and threshold scans, for tuning the rule thresholds from real outcomes.</summary>
public static class FeatureBinReport
{
    /// <summary>Bins with the given edges; the last bin is open-ended.</summary>
    public static IReadOnlyList<FeatureBin> Fixed(IEnumerable<(float Value, bool Label)> rows, IReadOnlyList<float> edges)
    {
        var bins = edges.Select((lower, i) => (lower, upper: i + 1 < edges.Count ? edges[i + 1] : float.PositiveInfinity)).ToList();
        var counts = new int[bins.Count];
        var positives = new int[bins.Count];
        foreach (var (value, label) in rows)
        {
            var i = bins.FindLastIndex(b => value >= b.lower);
            if (i < 0)
                continue;

            counts[i]++;
            if (label)
                positives[i]++;
        }

        return bins.Select((b, i) => new FeatureBin(b.lower, b.upper, counts[i], positives[i])).ToList();
    }

    /// <summary>Roughly equal-count bins at the feature's quantiles.</summary>
    public static IReadOnlyList<FeatureBin> Quantiles(IEnumerable<(float Value, bool Label)> rows, int binCount)
    {
        var list = rows.ToList();
        if (list.Count == 0)
            return [];

        var sorted = list.Select(r => r.Value).Order().ToList();
        var edges = Enumerable.Range(0, binCount)
            .Select(i => sorted[(int)((long)i * sorted.Count / binCount)])
            .Distinct()
            .ToList();
        return Fixed(list, edges);
    }

    /// <summary>
    /// Scans candidate cuts and returns the one with the largest chi-square between "value below the cut"
    /// and the label, among cuts with at least <paramref name="minPerSide"/> rows on each side.
    /// </summary>
    public static ThresholdScan? BestThreshold(IEnumerable<(float Value, bool Label)> rows, IEnumerable<float> candidates, int minPerSide = 50)
    {
        var list = rows.ToList();
        ThresholdScan? best = null;
        foreach (var cut in candidates)
        {
            int below = 0, belowPositive = 0, above = 0, abovePositive = 0;
            foreach (var (value, label) in list)
            {
                if (value < cut)
                {
                    below++;
                    if (label) belowPositive++;
                }
                else
                {
                    above++;
                    if (label) abovePositive++;
                }
            }

            if (below < minPerSide || above < minPerSide)
                continue;

            var chi = ChiSquare(belowPositive, below - belowPositive, abovePositive, above - abovePositive);
            if (best is null || chi > best.ChiSquare)
                best = new ThresholdScan(cut, below, belowPositive / (double)below, above, abovePositive / (double)above, chi);
        }

        return best;
    }

    public static (double Low, double High) Wilson(int positives, int count, double z = 1.96)
    {
        if (count == 0)
            return (double.NaN, double.NaN);

        var p = positives / (double)count;
        var denominator = 1 + z * z / count;
        var center = (p + z * z / (2 * count)) / denominator;
        var half = z * Math.Sqrt(p * (1 - p) / count + z * z / (4.0 * count * count)) / denominator;
        return (center - half, center + half);
    }

    private static double ChiSquare(int a, int b, int c, int d)
    {
        double n = a + b + c + d;
        var denominator = (double)(a + b) * (c + d) * (a + c) * (b + d);
        return denominator == 0 ? 0 : n * Math.Pow((double)a * d - (double)b * c, 2) / denominator;
    }
}
