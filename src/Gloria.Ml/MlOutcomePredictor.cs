using Gloria.Core.Classification;
using Microsoft.ML;

namespace Gloria.Ml;

/// <param name="Costs">Features that pushed the probability down, most harmful first ("what cost the duel").</param>
public sealed record OutcomePrediction(OutcomeTarget Target, float Probability, IReadOnlyList<FeatureContribution> Costs);

/// <summary>Predicts the outcome targets for a batch of encounters. Calls are serialized; PredictionEngine is not thread-safe.</summary>
public sealed class MlOutcomePredictor(OutcomeModelBundle bundle)
{
    private readonly MLContext _ml = new();
    private readonly Lock _lock = new();
    private readonly Dictionary<OutcomeTarget, IReadOnlyList<string>> _features =
        bundle.Manifest.Targets.ToDictionary(t => t.Target, t => t.FeatureNames);

    public OutcomeModelManifest Manifest => bundle.Manifest;

    /// <summary>One list per encounter, with a prediction for each target the bundle has a model for.</summary>
    public IReadOnlyList<IReadOnlyList<OutcomePrediction>> Predict(IReadOnlyList<EncounterFeatures> encounters)
    {
        var results = encounters.Select(_ => new List<OutcomePrediction>()).ToList();
        if (encounters.Count == 0)
            return results;

        lock (_lock)
        {
            var data = _ml.Data.LoadFromEnumerable(encounters.Select(e => MlEncounterRow.From(e)).ToList());
            foreach (var (target, model) in bundle.Models)
            {
                var names = _features[target];
                var row = 0;
                foreach (var prediction in _ml.Data.CreateEnumerable<MlPredictionRow>(model.Transform(data), reuseRowObject: false))
                {
                    var costs = prediction.FeatureContributions
                        .Select((value, slot) => new FeatureContribution(names[slot], value))
                        .Where(c => c.Contribution < 0f)
                        .OrderBy(c => c.Contribution)
                        .ToList();
                    results[row++].Add(new OutcomePrediction(target, prediction.Probability, costs));
                }
            }
        }

        return results;
    }
}
