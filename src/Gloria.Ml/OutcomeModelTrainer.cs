using System.Reflection;
using Gloria.Core.Classification;
using Microsoft.ML;

namespace Gloria.Ml;

/// <summary>An encounter with its objective outcome labels; a null label leaves it out of that target's training.</summary>
public sealed record OutcomeTrainingEncounter(EncounterFeatures Features, string DemoHash, OutcomeLabels Labels)
{
    public bool? Label(OutcomeTarget target) => target switch
    {
        OutcomeTarget.FirstShotHit => Labels.FirstShotHit,
        OutcomeTarget.WonDuel => Labels.WonDuel,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };
}

/// <summary>How much the held-out AUC drops when one feature's values are shuffled, averaged over the folds.</summary>
public sealed record FeatureImportance(string Feature, double AucDrop);

public sealed record OutcomeModelInfo(
    OutcomeTarget Target,
    IReadOnlyList<string> FeatureNames,
    int TrainingRows,
    int PositiveRows,
    CrossValidationSummary CrossValidation,
    IReadOnlyList<FeatureImportance> Importance);

public sealed record OutcomeModelManifest(
    int FormatVersion,
    int FeatureSchemaVersion,
    string ModelVersion,
    DateTimeOffset TrainedAt,
    IReadOnlyList<OutcomeModelInfo> Targets)
{
    public const int CurrentFormatVersion = 1;
}

public sealed record OutcomeModelBundle(
    OutcomeModelManifest Manifest,
    IReadOnlyDictionary<OutcomeTarget, ITransformer> Models,
    DataViewSchema InputSchema);

/// <summary>
/// Trains one FastTree model per <see cref="OutcomeTarget"/> on labels read from the demos, evaluated
/// leave-one-demo-out, with permutation feature importance on the held-out folds.
/// </summary>
public sealed class OutcomeModelTrainer(AimTrainingOptions? options = null)
{
    private static readonly Dictionary<string, PropertyInfo> Columns =
        MlEncounterRow.FeatureNames.ToDictionary(n => n, n => typeof(MlEncounterRow).GetProperty(n)!);

    private readonly AimTrainingOptions _options = options ?? new AimTrainingOptions();
    private readonly BinaryModelFitter _fitter = AimModelTrainer.Fitter(options ?? new AimTrainingOptions());

    public OutcomeModelBundle Train(IReadOnlyList<OutcomeTrainingEncounter> encounters, DateTimeOffset? trainedAt = null)
    {
        var ml = new MLContext(_options.Seed);
        var models = new Dictionary<OutcomeTarget, ITransformer>();
        var infos = new List<OutcomeModelInfo>();
        DataViewSchema? inputSchema = null;

        foreach (var target in Enum.GetValues<OutcomeTarget>())
        {
            var rows = encounters
                .Where(e => e.Label(target) is not null)
                .Select(e => MlEncounterRow.From(e.Features, e.Label(target)!.Value, 1f, e.DemoHash))
                .ToList();
            var positives = rows.Count(r => r.Label);
            if (positives == 0 || positives == rows.Count)
                continue;

            var features = OutcomeFeatures.For(target);
            var data = ml.Data.LoadFromEnumerable(rows);
            inputSchema ??= data.Schema;

            var (summary, importance) = CrossValidate(ml, rows, features);
            models[target] = Fit(ml, data, features);
            infos.Add(new OutcomeModelInfo(target, features, rows.Count, positives, summary, importance));
        }

        if (inputSchema is null)
            throw new InvalidOperationException("No outcome target had both positive and negative examples to train on.");

        var at = trainedAt ?? DateTimeOffset.UtcNow;
        var manifest = new OutcomeModelManifest(
            OutcomeModelManifest.CurrentFormatVersion,
            EncounterFeatures.SchemaVersion,
            at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'"),
            at,
            infos);
        return new OutcomeModelBundle(manifest, models, inputSchema);
    }

    // Negative contributions are what the UI shows: the features that pushed the outcome toward a loss.
    private ITransformer Fit(MLContext ml, IDataView data, IReadOnlyList<string> features) =>
        _fitter.Fit(ml, data, features, _options.TopContributions, _options.TopContributions);

    private (CrossValidationSummary, IReadOnlyList<FeatureImportance>) CrossValidate(MLContext ml, List<MlEncounterRow> rows, IReadOnlyList<string> features)
    {
        var folds = new List<FoldScores>();
        var drops = features.ToDictionary(f => f, _ => new List<double>());
        var random = new Random(_options.Seed);

        foreach (var (heldOut, train, test) in CrossValidation.LeaveOneGroupOut(rows, r => r.DemoHash))
        {
            if (!train.Any(r => r.Label) || !train.Any(r => !r.Label))
                continue;

            var model = Fit(ml, ml.Data.LoadFromEnumerable(train), features);
            var scores = BinaryModelFitter.Evaluate(ml, model, ml.Data.LoadFromEnumerable(test));
            folds.Add(new FoldScores(heldOut, scores));
            if (scores.Auc is not { } auc)
                continue;

            foreach (var feature in features)
            {
                var permuted = BinaryModelFitter.Evaluate(ml, model, ml.Data.LoadFromEnumerable(Permute(test, feature, random)));
                if (permuted.Auc is { } permutedAuc)
                    drops[feature].Add(auc - permutedAuc);
            }
        }

        var importance = drops
            .Where(d => d.Value.Count > 0)
            .Select(d => new FeatureImportance(d.Key, d.Value.Average()))
            .OrderByDescending(i => i.AucDrop)
            .ToList();
        return (new CrossValidationSummary(folds), importance);
    }

    private static List<MlEncounterRow> Permute(List<MlEncounterRow> rows, string feature, Random random)
    {
        var column = Columns[feature];
        var values = rows.Select(r => (float)column.GetValue(r)!).ToArray();
        random.Shuffle(values);
        return rows.Select((r, i) =>
        {
            var copy = (MlEncounterRow)r.Clone();
            column.SetValue(copy, values[i]);
            return copy;
        }).ToList();
    }
}
