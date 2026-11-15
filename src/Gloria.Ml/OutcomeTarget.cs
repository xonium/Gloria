namespace Gloria.Ml;

/// <summary>Objective outcomes the outcome models predict from the aim features.</summary>
public enum OutcomeTarget
{
    /// <summary>The first shot damaged the opponent.</summary>
    FirstShotHit,

    /// <summary>The player killed the opponent rather than dying to them.</summary>
    WonDuel,
}

public static class OutcomeFeatures
{
    // IsKill is the WonDuel label itself. The others are partly measured after the first shot.
    private static readonly HashSet<string> PostShot =
    [
        nameof(MlEncounterRow.TimeToOnTarget),
        nameof(MlEncounterRow.ReachedTarget),
        nameof(MlEncounterRow.ShotsWhileMovingRatio),
    ];

    /// <summary>
    /// Feature columns a target may see. Anything derived from the outcome is left out, so the model
    /// cannot read the answer from its inputs.
    /// </summary>
    public static IReadOnlyList<string> For(OutcomeTarget target) => target switch
    {
        OutcomeTarget.WonDuel => [.. MlEncounterRow.FeatureNames.Where(f => f != nameof(MlEncounterRow.IsKill))],
        OutcomeTarget.FirstShotHit => [.. MlEncounterRow.FeatureNames.Where(f => f != nameof(MlEncounterRow.IsKill) && !PostShot.Contains(f))],
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };
}
