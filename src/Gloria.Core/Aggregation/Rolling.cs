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
}
