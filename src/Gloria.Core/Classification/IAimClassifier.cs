namespace Gloria.Core.Classification;

/// <summary>One feature's share in a category decision, for showing the user why it was flagged.</summary>
public sealed record FeatureContribution(string Feature, float Contribution);

public sealed record CategoryScore(
    AimCategory Category,
    float Probability,
    bool Flagged,
    IReadOnlyList<FeatureContribution> TopContributions);

public sealed record AimClassification(IReadOnlyList<CategoryScore> Scores)
{
    public bool IsClean => !Scores.Any(s => s.Flagged);

    /// <summary>The flagged category with the highest probability, or null when clean.</summary>
    public AimCategory? Primary => Scores
        .Where(s => s.Flagged)
        .OrderByDescending(s => s.Probability)
        .Select(s => (AimCategory?)s.Category)
        .FirstOrDefault();

    public CategoryScore this[AimCategory category] => Scores.Single(s => s.Category == category);
}

public interface IAimClassifier
{
    /// <summary>Where the decisions come from, e.g. "rules" or a model version. Stored with each label.</summary>
    string Source { get; }

    /// <summary>Classifies a batch; the result has one entry per input, in order.</summary>
    IReadOnlyList<AimClassification> Classify(IReadOnlyList<EncounterFeatures> encounters);
}
