namespace Gloria.Ml;

/// <summary>One held-out group's scores.</summary>
public sealed record FoldScores(string HeldOut, BinaryScores Scores);

/// <summary>Leave-one-group-out results with means and standard deviations over the folds that had both classes.</summary>
public sealed record CrossValidationSummary(IReadOnlyList<FoldScores> Folds)
{
    public double? MeanAuc => Mean(f => f.Auc);
    public double? SdAuc => Sd(f => f.Auc);
    public double? MeanLogLoss => Mean(f => f.LogLoss);
    public double? MeanBrier => Mean(f => f.Brier);
    public double? MeanF1 => Mean(f => f.F1);

    private double? Mean(Func<BinaryScores, double?> metric)
    {
        var values = Folds.Select(f => metric(f.Scores)).OfType<double>().ToList();
        return values.Count > 0 ? values.Average() : null;
    }

    private double? Sd(Func<BinaryScores, double?> metric)
    {
        var values = Folds.Select(f => metric(f.Scores)).OfType<double>().ToList();
        if (values.Count < 2)
            return null;

        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }
}

public static class CrossValidation
{
    /// <summary>
    /// One fold per distinct group key: train on every other group, test on that one. With few demos,
    /// ML.NET's CrossValidate hashes keys into folds, so some folds would merge or come out empty.
    /// Grouping by demo keeps both mirrored rows of a duel (the killer's and the victim's) on the same side.
    /// </summary>
    public static IEnumerable<(string HeldOut, List<T> Train, List<T> Test)> LeaveOneGroupOut<T>(IReadOnlyList<T> rows, Func<T, string> group)
    {
        foreach (var key in rows.Select(group).Distinct().Order(StringComparer.Ordinal))
        {
            var train = rows.Where(r => group(r) != key).ToList();
            var test = rows.Where(r => group(r) == key).ToList();
            yield return (key, train, test);
        }
    }
}
