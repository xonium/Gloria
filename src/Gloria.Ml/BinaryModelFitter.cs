using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;

namespace Gloria.Ml;

/// <summary>Scores of a binary model on held-out rows. Null when the rows lack a class or the value is not finite.</summary>
public sealed record BinaryScores(int Rows, double BaseRate, double? Auc, double? F1, double? LogLoss, double? Brier);

/// <summary>
/// Fits and evaluates the FastTree binary pipeline shared by the aim and outcome models:
/// Concatenate(features) → FastTree(Label, Weight) → CalculateFeatureContribution.
/// </summary>
internal sealed class BinaryModelFitter(int numberOfTrees, int numberOfLeaves, int minimumExampleCountPerLeaf)
{
    private const string FeaturesColumn = "Features";

    /// <param name="features">Columns of <see cref="MlEncounterRow"/> to use; contribution slot i is features[i].</param>
    public ITransformer Fit(MLContext ml, IDataView data, IReadOnlyList<string> features, int positiveContributions, int negativeContributions)
    {
        var featurizer = ml.Transforms.Concatenate(FeaturesColumn, [.. features]).Fit(data);
        var featurized = featurizer.Transform(data);

        var model = ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
        {
            LabelColumnName = nameof(MlEncounterRow.Label),
            FeatureColumnName = FeaturesColumn,
            ExampleWeightColumnName = nameof(MlEncounterRow.Weight),
            NumberOfTrees = numberOfTrees,
            NumberOfLeaves = numberOfLeaves,
            MinimumExampleCountPerLeaf = minimumExampleCountPerLeaf,
        }).Fit(featurized);

        var contributions = ml.Transforms
            .CalculateFeatureContribution(model, positiveContributions, negativeContributions)
            .Fit(model.Transform(featurized));

        return featurizer.Append(model).Append(contributions);
    }

    public static BinaryScores Evaluate(MLContext ml, ITransformer model, IDataView test)
    {
        var scored = ml.Data.CreateEnumerable<ScoredRow>(model.Transform(test), reuseRowObject: false).ToList();
        if (scored.Count == 0)
            return new BinaryScores(0, 0, null, null, null, null);

        var positives = scored.Count(r => r.Label);
        var baseRate = positives / (double)scored.Count;
        var brier = scored.Average(r => Math.Pow(r.Probability - (r.Label ? 1 : 0), 2));
        if (positives == 0 || positives == scored.Count)
            return new BinaryScores(scored.Count, baseRate, null, null, null, Finite(brier));

        var metrics = ml.BinaryClassification.Evaluate(model.Transform(test), labelColumnName: nameof(MlEncounterRow.Label));
        return new BinaryScores(
            scored.Count,
            baseRate,
            Finite(metrics.AreaUnderRocCurve),
            Finite(metrics.F1Score),
            Finite(metrics.LogLoss),
            Finite(brier));
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    private sealed class ScoredRow
    {
        public bool Label { get; set; }
        public float Probability { get; set; }
    }
}
