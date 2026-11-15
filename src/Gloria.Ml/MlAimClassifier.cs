using Gloria.Core.Classification;
using Microsoft.ML;

namespace Gloria.Ml;

/// <summary>
/// Classifies with a trained bundle, a whole batch per call (PredictionEngine is not thread-safe,
/// and batch transforms are faster). Categories missing from the bundle use the rules.
/// </summary>
public sealed class MlAimClassifier(AimModelBundle bundle) : IAimClassifier
{
    private const float FlagThreshold = 0.5f;

    private readonly MLContext _ml = new();
    private readonly Lock _lock = new();

    public string Source => $"model {bundle.Manifest.ModelVersion}";

    public AimModelManifest Manifest => bundle.Manifest;

    public IReadOnlyList<AimClassification> Classify(IReadOnlyList<EncounterFeatures> encounters)
    {
        if (encounters.Count == 0)
            return [];

        var scores = encounters.Select(_ => new List<CategoryScore>()).ToList();
        lock (_lock)
        {
            var data = _ml.Data.LoadFromEnumerable(encounters.Select(e => MlEncounterRow.From(e)).ToList());
            foreach (var category in Enum.GetValues<AimCategory>())
            {
                if (!bundle.Models.TryGetValue(category, out var model))
                {
                    for (var i = 0; i < encounters.Count; i++)
                        scores[i].Add(RuleBasedAimClassifier.Score(encounters[i], category));
                    continue;
                }

                var predictions = _ml.Data.CreateEnumerable<MlPredictionRow>(model.Transform(data), reuseRowObject: false);
                var row = 0;
                foreach (var prediction in predictions)
                {
                    scores[row++].Add(new CategoryScore(
                        category,
                        prediction.Probability,
                        prediction.Probability >= FlagThreshold,
                        TopContributions(prediction)));
                }
            }
        }

        return scores.Select(s => new AimClassification(s)).ToList();
    }

    private static List<FeatureContribution> TopContributions(MlPredictionRow prediction) => prediction.FeatureContributions
        .Select((value, slot) => new FeatureContribution(MlEncounterRow.FeatureNames[slot], value))
        .Where(c => c.Contribution > 0f)
        .OrderByDescending(c => c.Contribution)
        .ToList();
}
