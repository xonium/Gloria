using Gloria.Core.Metrics;

namespace Gloria.Core.Classification;

/// <summary>
/// Hand-written thresholds that give each encounter its first labels. They are the fallback
/// classifier and the weak labels the ML models start from; user corrections override them.
/// The constants were checked against outcomes in real demos with the trainer report
/// (tools/Gloria.Trainer, docs/ml-classification.md); OnTargetBodyHalfWidths was tuned from it.
/// </summary>
public static class RuleBasedAimLabeler
{
    public const float OvershootDegrees = 1.5f;
    public const int UndershootCorrections = 2;
    public const float MovingSpeedRatio = 1f;

    /// <summary>
    /// The first shot is hasty when the crosshair was more than this many body half-widths from the body axis,
    /// i.e. off the body. A fixed angle does not work: the best cut on real demos fell from 3.5° at close range to 0.5° at long range.
    /// </summary>
    public const float OnTargetBodyHalfWidths = 1f;
    public const float SlowReactionSeconds = 0.6f;

    public static bool IsFlagged(EncounterFeatures features, AimCategory category) => category switch
    {
        AimCategory.Overshoot => features.PeakOvershootDegrees >= OvershootDegrees,
        AimCategory.Undershoot => features.PeakOvershootDegrees < OvershootDegrees
            && features.CorrectionCount >= UndershootCorrections,
        AimCategory.MovingShot => features.SpeedRatioAtFirstShot > MovingSpeedRatio,
        AimCategory.Hasty => features.AngularErrorAtFirstShot > OnTargetDegreesAt(features.DistanceToEnemy),
        AimCategory.SlowReaction => features.TimeToFirstShot > SlowReactionSeconds,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>The on-target tolerance at the given distance, in degrees.</summary>
    public static float OnTargetDegreesAt(float distance) => OnTargetBodyHalfWidths * BodyModel.HalfWidthDegrees(distance);

    public static IReadOnlySet<AimCategory> Label(EncounterFeatures features) =>
        Enum.GetValues<AimCategory>().Where(c => IsFlagged(features, c)).ToHashSet();
}
