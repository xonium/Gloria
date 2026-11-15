using Gloria.Core.Weapons;

namespace Gloria.Core.Classification;

/// <summary>
/// Fixed-size description of one frag or death, used both by the rules and by the ML models.
/// Times are in seconds from first visibility, angles in degrees.
/// </summary>
/// <param name="TimeToOnTarget">Null when the crosshair never reached the target.</param>
/// <param name="SpeedRatioAtFirstShot">
/// Speed at the first shot divided by the weapon's accurate-speed threshold
/// (<see cref="WeaponMovementData.AccurateSpeedThreshold"/>). Above 1 means the shot was inaccurate from movement.
/// </param>
public sealed record EncounterFeatures(
    float TimeToFirstShot,
    float? TimeToOnTarget,
    float AngularErrorAtFirstVisible,
    float AngularErrorAtFirstShot,
    float PeakOvershootDegrees,
    int CorrectionCount,
    float FlickDistanceDegrees,
    float SpeedRatioAtFirstShot,
    float ShotsWhileMovingRatio,
    float DistanceToEnemy,
    WeaponClass WeaponClass,
    bool IsKill)
{
    /// <summary>Bump when fields are added, removed or change meaning. Saved models with another version are refused.</summary>
    public const int SchemaVersion = 1;
}
