namespace Gloria.Core.Classification;

/// <summary>Classifies with <see cref="RuleBasedAimLabeler"/>. Used when no trained model is available.</summary>
public sealed class RuleBasedAimClassifier : IAimClassifier
{
    public string Source => "rules";

    public IReadOnlyList<AimClassification> Classify(IReadOnlyList<EncounterFeatures> encounters) =>
        encounters.Select(Classify).ToList();

    public static AimClassification Classify(EncounterFeatures features) => new(
        Enum.GetValues<AimCategory>()
            .Select(category => Score(features, category))
            .ToList());

    public static CategoryScore Score(EncounterFeatures features, AimCategory category)
    {
        var flagged = RuleBasedAimLabeler.IsFlagged(features, category);
        return new CategoryScore(category, flagged ? 1f : 0f, flagged, []);
    }
}
