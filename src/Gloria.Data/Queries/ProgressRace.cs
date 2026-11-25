namespace Gloria.Data.Queries;

public enum Improvement
{
    /// <summary>No player has two medians far enough apart to say.</summary>
    NotEnoughDemos,

    /// <summary>Every player who could be measured stayed level or got worse.</summary>
    NoOneImproved,

    /// <summary><see cref="ProgressStanding.MostImproved"/> got better the most.</summary>
    Improved,
}

/// <summary>Who is ahead on one metric now, and who improved the most over the period.</summary>
/// <param name="Leaders">Players with the best latest median, as indexes into the input; several on a tie, none when no player has one.</param>
/// <param name="Best">The leaders' latest median; null when there are no leaders.</param>
/// <param name="MostImproved">The player who improved the most; null unless <paramref name="Improvement"/> is <see cref="Improvement.Improved"/>.</param>
/// <param name="Change">That player's latest median minus their first, in the metric's unit (so negative when lower is better).</param>
public sealed record ProgressStanding(IReadOnlyList<int> Leaders, double? Best, int? MostImproved, double? Change, Improvement Improvement);

/// <summary>Ranks several players' trends on one metric, for the Progress page's "current leader" and "most improved".</summary>
public static class ProgressRace
{
    /// <param name="trends">Each player's rolling median per demo, oldest first, NaN where unknown.</param>
    /// <param name="higherIsBetter">Whether a higher value is better, e.g. false for a time.</param>
    /// <param name="window">
    /// The demos in each median. A player counts for "most improved" only when their first and latest medians are at
    /// least this many demos apart, so the two share no demo.
    /// </param>
    public static ProgressStanding Rank(IReadOnlyList<IReadOnlyList<double>> trends, bool higherIsBetter, int window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
        var sign = higherIsBetter ? 1 : -1;

        var latest = new List<(int Player, double Value)>();
        var changes = new List<(int Player, double Change)>();
        for (var p = 0; p < trends.Count; p++)
        {
            var trend = trends[p];
            var first = FirstKnown(trend);
            var last = LastKnown(trend);
            if (first < 0)
                continue;

            latest.Add((p, trend[last]));
            if (last - first >= window)
                changes.Add((p, trend[last] - trend[first]));
        }

        IReadOnlyList<int> leaders = [];
        double? best = null;
        if (latest.Count > 0)
        {
            var top = latest.Max(l => sign * l.Value);
            leaders = latest.Where(l => sign * l.Value >= top - 1e-9).Select(l => l.Player).ToList();
            best = sign * top;
        }

        if (changes.Count == 0)
            return new ProgressStanding(leaders, best, null, null, Improvement.NotEnoughDemos);

        var most = changes.MaxBy(c => sign * c.Change);
        return sign * most.Change > 1e-9
            ? new ProgressStanding(leaders, best, most.Player, most.Change, Improvement.Improved)
            : new ProgressStanding(leaders, best, null, null, Improvement.NoOneImproved);
    }

    private static int FirstKnown(IReadOnlyList<double> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (!double.IsNaN(values[i]))
                return i;
        }

        return -1;
    }

    private static int LastKnown(IReadOnlyList<double> values)
    {
        for (var i = values.Count - 1; i >= 0; i--)
        {
            if (!double.IsNaN(values[i]))
                return i;
        }

        return -1;
    }
}
