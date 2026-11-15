using Gloria.Core.Classification;
using Microsoft.ML;

namespace Gloria.Ml;

/// <summary>Training statistics for one category model, kept in the bundle manifest.</summary>
/// <param name="Auc">Area under the ROC curve on the held-out demos; null when the split lacked a class.</param>
public sealed record CategoryModelInfo(
    AimCategory Category,
    int TrainingRows,
    int PositiveRows,
    int CorrectedRows,
    double? Auc,
    double? F1);

public sealed record AimModelManifest(
    int FormatVersion,
    int FeatureSchemaVersion,
    string ModelVersion,
    DateTimeOffset TrainedAt,
    IReadOnlyList<CategoryModelInfo> Categories)
{
    public const int CurrentFormatVersion = 1;
}

/// <summary>
/// One binary model per category. A category without a model (too few examples of one class)
/// falls back to the rules.
/// </summary>
public sealed record AimModelBundle(
    AimModelManifest Manifest,
    IReadOnlyDictionary<AimCategory, ITransformer> Models,
    DataViewSchema InputSchema);
