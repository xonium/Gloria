using Gloria.Core.Classification;
using Microsoft.ML;

namespace Gloria.Ml;

/// <summary>An encounter to train on. Categories without a correction get their label from the rules.</summary>
public sealed record TrainingEncounter(
    EncounterFeatures Features,
    string DemoHash,
    IReadOnlyDictionary<AimCategory, bool>? Corrections = null);

public enum EvaluationMode
{
    /// <summary>Leave-one-demo-out below <see cref="AimTrainingOptions.LeaveOneOutBelowDemos"/> demos, otherwise a hold-out split.</summary>
    Auto,

    /// <summary>Hold out <see cref="AimTrainingOptions.TestFraction"/> of the demos.</summary>
    HoldOut,

    /// <summary>One fold per demo; the reported score is the mean over folds.</summary>
    LeaveOneDemoOut,
}

public sealed record AimTrainingOptions
{
    public int Seed { get; init; } = 1;

    /// <summary>Example weight of a user-corrected label, relative to 1 for a rule label.</summary>
    public float CorrectionWeight { get; init; } = 5f;

    /// <summary>Fraction of demos (not rows) held out for evaluation.</summary>
    public double TestFraction { get; init; } = 0.2;

    public EvaluationMode Evaluation { get; init; } = EvaluationMode.Auto;

    /// <summary>With fewer demos, a 20% hold-out is a single demo, so <see cref="EvaluationMode.Auto"/> uses leave-one-demo-out.</summary>
    public int LeaveOneOutBelowDemos { get; init; } = 10;

    public int NumberOfTrees { get; init; } = 100;
    public int NumberOfLeaves { get; init; } = 16;
    public int MinimumExampleCountPerLeaf { get; init; } = 5;

    /// <summary>How many features each prediction explains itself with.</summary>
    public int TopContributions { get; init; } = 3;
}

/// <summary>Trains one FastTree binary classifier per <see cref="AimCategory"/>.</summary>
public sealed class AimModelTrainer(AimTrainingOptions? options = null)
{
    private readonly AimTrainingOptions _options = options ?? new AimTrainingOptions();
    private readonly BinaryModelFitter _fitter = Fitter(options ?? new AimTrainingOptions());

    internal static BinaryModelFitter Fitter(AimTrainingOptions options) =>
        new(options.NumberOfTrees, options.NumberOfLeaves, options.MinimumExampleCountPerLeaf);

    /// <summary>Whether to evaluate with one fold per demo, given the options and the number of distinct demos.</summary>
    internal static bool UseLeaveOneDemoOut(AimTrainingOptions options, int demoCount) => options.Evaluation switch
    {
        EvaluationMode.LeaveOneDemoOut => true,
        EvaluationMode.HoldOut => false,
        _ => demoCount < options.LeaveOneOutBelowDemos,
    };

    public AimModelBundle Train(IReadOnlyList<TrainingEncounter> encounters, DateTimeOffset? trainedAt = null)
    {
        var ml = new MLContext(_options.Seed);
        var models = new Dictionary<AimCategory, ITransformer>();
        var infos = new List<CategoryModelInfo>();
        DataViewSchema? inputSchema = null;

        foreach (var category in Enum.GetValues<AimCategory>())
        {
            var rows = encounters.Select(e => ToRow(e, category)).ToList();
            var positives = rows.Count(r => r.Label);
            if (positives == 0 || positives == rows.Count)
                continue;

            var data = ml.Data.LoadFromEnumerable(rows);
            inputSchema ??= data.Schema;

            // Evaluate on held-out demos, then train the model that is kept on everything.
            var (auc, f1) = Evaluate(ml, rows, data);

            models[category] = Fit(ml, data);
            infos.Add(new CategoryModelInfo(
                category,
                rows.Count,
                positives,
                encounters.Count(e => e.Corrections?.ContainsKey(category) == true),
                auc,
                f1));
        }

        if (inputSchema is null)
            throw new InvalidOperationException("No category had both positive and negative examples to train on.");

        var at = trainedAt ?? DateTimeOffset.UtcNow;
        var manifest = new AimModelManifest(
            AimModelManifest.CurrentFormatVersion,
            EncounterFeatures.SchemaVersion,
            at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'"),
            at,
            infos);
        return new AimModelBundle(manifest, models, inputSchema);
    }

    private MlEncounterRow ToRow(TrainingEncounter encounter, AimCategory category)
    {
        if (encounter.Corrections?.TryGetValue(category, out var corrected) == true)
            return MlEncounterRow.From(encounter.Features, corrected, _options.CorrectionWeight, encounter.DemoHash);

        var label = RuleBasedAimLabeler.IsFlagged(encounter.Features, category);
        return MlEncounterRow.From(encounter.Features, label, 1f, encounter.DemoHash);
    }

    private ITransformer Fit(MLContext ml, IDataView data) =>
        _fitter.Fit(ml, data, MlEncounterRow.FeatureNames, _options.TopContributions, 0);

    private (double? Auc, double? F1) Evaluate(MLContext ml, List<MlEncounterRow> rows, IDataView data)
    {
        if (UseLeaveOneDemoOut(_options, rows.Select(r => r.DemoHash).Distinct().Count()))
        {
            var folds = CrossValidation.LeaveOneGroupOut(rows, r => r.DemoHash)
                .Where(f => f.Train.Any(r => r.Label) && f.Train.Any(r => !r.Label))
                .Select(f => new FoldScores(f.HeldOut, BinaryModelFitter.Evaluate(
                    ml, Fit(ml, ml.Data.LoadFromEnumerable(f.Train)), ml.Data.LoadFromEnumerable(f.Test))))
                .ToList();
            var summary = new CrossValidationSummary(folds);
            return (summary.MeanAuc, summary.MeanF1);
        }

        var split = ml.Data.TrainTestSplit(data, _options.TestFraction, samplingKeyColumnName: nameof(MlEncounterRow.DemoHash), seed: _options.Seed);
        var scores = BinaryModelFitter.Evaluate(ml, Fit(ml, split.TrainSet), split.TestSet);
        return (scores.Auc, scores.F1);
    }
}
