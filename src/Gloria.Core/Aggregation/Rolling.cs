namespace Gloria.Core.Aggregation;

/// <summary>Smoothing for per-demo series, so one odd match doesn't swing a trend.</summary>
public static class Rolling
{
    /// <summary>
    /// Trailing median of each value and the ones before it, over up to <paramref name="window"/> values, skipping
    /// NaN. A point stays NaN until more than half a window of values is known (fewer when the series is short).
    /// </summary>
    public static double[] Median(IReadOnlyList<double> values, int window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
        var known = values.Count(v => !double.IsNaN(v));
        var needed = Math.Min(window, Math.Max(1, known)) / 2 + 1;
        var result = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var recent = values.Skip(Math.Max(0, i + 1 - window)).Take(Math.Min(window, i + 1)).Where(v => !double.IsNaN(v)).Order().ToList();
            result[i] = recent.Count < needed ? double.NaN : recent[recent.Count / 2];
        }

        return result;
    }

    /// <summary>
    /// Trailing median of the samples in each demo and the demos before it, over up to <paramref name="window"/>
    /// demos. For sparse numbers, e.g. kills with one gun, where most demos have a sample or two and a median of
    /// per-demo medians would rest on almost nothing. NaN where fewer than <paramref name="minSamples"/> are pooled.
    /// </summary>
    public static double[] PooledMedian(IReadOnlyList<IReadOnlyList<double>> samplesPerDemo, int window, int minSamples)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minSamples, 1);
        var result = new double[samplesPerDemo.Count];
        for (var i = 0; i < samplesPerDemo.Count; i++)
        {
            var pooled = samplesPerDemo
                .Skip(Math.Max(0, i + 1 - window))
                .Take(Math.Min(window, i + 1))
                .SelectMany(s => s)
                .Where(v => !double.IsNaN(v))
                .Order()
                .ToList();
            result[i] = pooled.Count < minSamples ? double.NaN : pooled[pooled.Count / 2];
        }

        return result;
    }

    /// <summary>The median of the known values, the upper one of an even count as elsewhere; NaN when none are known.</summary>
    public static double MedianOf(IEnumerable<double> values)
    {
        var sorted = values.Where(v => !double.IsNaN(v)).Order().ToList();
        return sorted.Count == 0 ? double.NaN : sorted[sorted.Count / 2];
    }
}
