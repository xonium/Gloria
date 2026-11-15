using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;

namespace Gloria.Core.Metrics;

public enum AimApproach
{
    Unknown,
    OnTarget,
    Overshoot,
    Undershoot,
}

/// <summary>
/// Per-encounter aim scores. Times are in seconds from the encounter start (first contact), angles in
/// degrees. Overshoot, corrections and flick distance describe the approach window: from the start of
/// the view movement that led to the first shot, up to that shot.
/// </summary>
/// <param name="TimeToOnTarget">Null when the crosshair never reached the target.</param>
/// <param name="SpeedAtShots">Horizontal speed at each attributed shot.</param>
/// <param name="SpeedRatioAtFirstShot">Speed at the first shot over the weapon's accurate-speed threshold.</param>
/// <param name="AngularErrorAtFirstShotTick">Error at the recorded tick of the first shot, without sub-tick interpolation.</param>
/// <param name="AngularErrorAtTickBeforeFirstShot">Error one tick before the first shot.</param>
/// <param name="AngularErrorTwoTicksBeforeFirstShot">Error two ticks before the first shot.</param>
/// <param name="ApproachStartTick">Where the view movement leading to the first shot began.</param>
/// <param name="PlayerHealthAtStart">The player's HP just before the encounter start.</param>
/// <param name="OpponentHealthAtStart">The opponent's HP just before the encounter start.</param>
/// <param name="OpponentWeapon">The opponent's active weapon on the tick before the player's first shot.</param>
/// <param name="OpponentFirstShotTick">The opponent's first shot at the player around this encounter, if any.</param>
public sealed record EncounterMetrics(
    Encounter Encounter,
    AimApproach Approach,
    double? TimeToFirstShot,
    double? TimeToOnTarget,
    IReadOnlyList<float> SpeedAtShots,
    int ShotsWhileMoving,
    int FirstShotTick,
    string FirstShotWeapon,
    IReadOnlyList<int> ShotTicks,
    float AngularErrorAtStart,
    float AngularErrorAtFirstShot,
    float PeakOvershootDegrees,
    int CorrectionCount,
    float FlickDistanceDegrees,
    float SpeedRatioAtFirstShot,
    float DistanceAtFirstShot,
    float AngularErrorAtFirstShotTick,
    float AngularErrorAtTickBeforeFirstShot,
    float AngularErrorTwoTicksBeforeFirstShot,
    int ApproachStartTick,
    int PlayerHealthAtStart,
    int OpponentHealthAtStart,
    string OpponentWeapon,
    int? OpponentFirstShotTick)
{
    /// <summary>The player shot no later than the opponent did, or the opponent never shot at them.</summary>
    public bool FiredFirst => OpponentFirstShotTick is not { } opponent || FirstShotTick <= opponent;
}

public interface IAimMetricsCalculator
{
    /// <summary>Bumped when metric logic changes, so stored metrics can be recomputed.</summary>
    int Version { get; }

    /// <summary>Null when the encounter has no attributed shot or the player's state is missing.</summary>
    EncounterMetrics? Calculate(ParsedDemo demo, Encounter encounter);
}
