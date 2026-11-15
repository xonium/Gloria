namespace Gloria.Core.Classification;

/// <summary>
/// Hand-written thresholds that give each encounter its first labels. They are the fallback
/// classifier and the weak labels the ML models start from; user corrections override them.
/// The values are initial guesses to be tuned against real demos.
/// </summary>
public static class RuleBasedAimLabeler
{
    public const float OvershootDegrees = 1.5f;
    public const int UndershootCorrections = 2;
    public const float MovingSpeedRatio = 1f;
    public const float OnTargetDegrees = 2f;
    public const float SlowReactionSeconds = 0.6f;

    public static bool IsFlagged(EncounterFeatures features, AimCategory category) => category switch
    {
        AimCategory.Overshoot => features.PeakOvershootDegrees >= OvershootDegrees,
        AimCategory.Undershoot => features.PeakOvershootDegrees < OvershootDegrees
            && features.CorrectionCount >= UndershootCorrections,
        AimCategory.MovingShot => features.SpeedRatioAtFirstShot > MovingSpeedRatio,
        AimCategory.Hasty => features.AngularErrorAtFirstShot > OnTargetDegrees,
        AimCategory.SlowReaction => features.TimeToFirstShot > SlowReactionSeconds,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    public static IReadOnlySet<AimCategory> Label(EncounterFeatures features) =>
        Enum.GetValues<AimCategory>().Where(c => IsFlagged(features, c)).ToHashSet();
}
