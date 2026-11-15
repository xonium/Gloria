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
/// <param name="ApproachStartTick">Where the view movement leading to the first shot began.</param>
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
    int ApproachStartTick);

public interface IAimMetricsCalculator
{
    /// <summary>Bumped when metric logic changes, so stored metrics can be recomputed.</summary>
    int Version { get; }

    /// <summary>Null when the encounter has no attributed shot or the player's state is missing.</summary>
    EncounterMetrics? Calculate(ParsedDemo demo, Encounter encounter);
}
