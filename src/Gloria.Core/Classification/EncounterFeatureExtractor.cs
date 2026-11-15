using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;
using Gloria.Core.Weapons;

namespace Gloria.Core.Classification;

/// <summary>Maps <see cref="EncounterMetrics"/> onto the fixed feature vector.</summary>
public sealed class EncounterFeatureExtractor(IWeaponData? weapons = null) : IEncounterFeatureExtractor
{
    private readonly IWeaponData _weapons = weapons ?? StaticWeaponData.Instance;

    public EncounterFeatures Extract(ParsedDemo demo, Encounter encounter, EncounterMetrics metrics) => new(
        TimeToFirstShot: (float)(metrics.TimeToFirstShot ?? 0),
        TimeToOnTarget: metrics.TimeToOnTarget is { } t ? (float)t : null,
        AngularErrorAtFirstVisible: metrics.AngularErrorAtStart,
        AngularErrorAtFirstShot: metrics.AngularErrorAtFirstShot,
        PeakOvershootDegrees: metrics.PeakOvershootDegrees,
        CorrectionCount: metrics.CorrectionCount,
        FlickDistanceDegrees: metrics.FlickDistanceDegrees,
        SpeedRatioAtFirstShot: metrics.SpeedRatioAtFirstShot,
        ShotsWhileMovingRatio: metrics.SpeedAtShots.Count > 0 ? metrics.ShotsWhileMoving / (float)metrics.SpeedAtShots.Count : 0f,
        DistanceToEnemy: metrics.DistanceAtFirstShot,
        WeaponClass: _weapons.Classify(metrics.FirstShotWeapon),
        IsKill: encounter.Outcome == EncounterOutcome.Kill);
}
