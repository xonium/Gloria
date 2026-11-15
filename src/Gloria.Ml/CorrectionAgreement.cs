using Gloria.Core.Classification;

namespace Gloria.Ml;

/// <summary>One user verdict to check a classifier against.</summary>
public sealed record CorrectedLabel(EncounterFeatures Features, AimCategory Category, bool Value);

/// <summary>How many of the user's verdicts a classifier agrees with.</summary>
public readonly record struct Agreement(int Agreed, int Total)
{
    public double Rate => Total == 0 ? 0 : (double)Agreed / Total;

    public override string ToString() => $"{Agreed} of {Total}";

    public static Agreement Of(IAimClassifier classifier, IReadOnlyList<CorrectedLabel> labels)
    {
        if (labels.Count == 0)
            return new Agreement(0, 0);

        var results = classifier.Classify(labels.Select(l => l.Features).ToList());
        var agreed = labels.Where((l, i) => results[i][l.Category].Flagged == l.Value).Count();
        return new Agreement(agreed, labels.Count);
    }
}
