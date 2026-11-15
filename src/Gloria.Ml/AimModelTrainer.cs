using Gloria.Core.Classification;
using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;

namespace Gloria.Ml;

/// <summary>An encounter to train on. Categories without a correction get their label from the rules.</summary>
public sealed record TrainingEncounter(
    EncounterFeatures Features,
    string DemoHash,
    IReadOnlyDictionary<AimCategory, bool>? Corrections = null);

public sealed record AimTrainingOptions
{
    public int Seed { get; init; } = 1;

    /// <summary>Example weight of a user-corrected label, relative to 1 for a rule label.</summary>
    public float CorrectionWeight { get; init; } = 5f;

    /// <summary>Fraction of demos (not rows) held out for evaluation.</summary>
    public double TestFraction { get; init; } = 0.2;

    public int NumberOfTrees { get; init; } = 100;
    public int NumberOfLeaves { get; init; } = 16;
    public int MinimumExampleCountPerLeaf { get; init; } = 5;

    /// <summary>How many features each prediction explains itself with.</summary>
    public int TopContributions { get; init; } = 3;
}

/// <summary>Trains one FastTree binary classifier per <see cref="AimCategory"/>.</summary>
public sealed class AimModelTrainer(AimTrainingOptions? options = null)
{
    private const string FeaturesColumn = "Features";

    private readonly AimTrainingOptions _options = options ?? new AimTrainingOptions();

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
            var split = ml.Data.TrainTestSplit(data, _options.TestFraction, samplingKeyColumnName: nameof(MlEncounterRow.DemoHash), seed: _options.Seed);
            var (auc, f1) = Evaluate(ml, Fit(ml, split.TrainSet), split.TestSet);

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

    private ITransformer Fit(MLContext ml, IDataView data)
    {
        var featurizer = ml.Transforms.Concatenate(FeaturesColumn, MlEncounterRow.FeatureNames).Fit(data);
        var featurized = featurizer.Transform(data);

        var model = ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
        {
            LabelColumnName = nameof(MlEncounterRow.Label),
            FeatureColumnName = FeaturesColumn,
            ExampleWeightColumnName = nameof(MlEncounterRow.Weight),
            NumberOfTrees = _options.NumberOfTrees,
            NumberOfLeaves = _options.NumberOfLeaves,
            MinimumExampleCountPerLeaf = _options.MinimumExampleCountPerLeaf,
        }).Fit(featurized);

        var contributions = ml.Transforms
            .CalculateFeatureContribution(model, numberOfPositiveContributions: _options.TopContributions, numberOfNegativeContributions: 0)
            .Fit(model.Transform(featurized));

        return featurizer.Append(model).Append(contributions);
    }

    private static (double? Auc, double? F1) Evaluate(MLContext ml, ITransformer model, IDataView test)
    {
        var labels = ml.Data.CreateEnumerable<MlEncounterRow>(test, reuseRowObject: false).Select(r => r.Label).ToList();
        if (labels.Count == 0 || labels.All(l => l) || labels.All(l => !l))
            return (null, null);

        var metrics = ml.BinaryClassification.Evaluate(model.Transform(test), labelColumnName: nameof(MlEncounterRow.Label));
        return (Finite(metrics.AreaUnderRocCurve), Finite(metrics.F1Score));
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
}
