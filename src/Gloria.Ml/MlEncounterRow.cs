using Gloria.Core.Classification;

namespace Gloria.Ml;

/// <summary>
/// ML.NET input schema: <see cref="EncounterFeatures"/> flattened to floats, plus the label,
/// example weight and the demo it came from (used to split train/test by match).
/// </summary>
public sealed class MlEncounterRow
{
    /// <summary>Stands in for <see cref="EncounterFeatures.TimeToOnTarget"/> when the target was never reached.</summary>
    public const float NeverOnTargetSeconds = 5f;

    /// <summary>The feature columns, in the order they are concatenated. Contribution slot i is FeatureNames[i].</summary>
    public static readonly string[] FeatureNames =
    [
        nameof(TimeToFirstShot),
        nameof(TimeToOnTarget),
        nameof(ReachedTarget),
        nameof(AngularErrorAtFirstVisible),
        nameof(AngularErrorAtFirstShot),
        nameof(PeakOvershootDegrees),
        nameof(CorrectionCount),
        nameof(FlickDistanceDegrees),
        nameof(SpeedRatioAtFirstShot),
        nameof(ShotsWhileMovingRatio),
        nameof(DistanceToEnemy),
        nameof(WeaponClass),
        nameof(IsKill),
        nameof(PlayerHealth),
        nameof(OpponentHealth),
        nameof(OpponentWeaponClass),
        nameof(FiredFirst),
    ];

    public float TimeToFirstShot { get; set; }
    public float TimeToOnTarget { get; set; }
    public float ReachedTarget { get; set; }
    public float AngularErrorAtFirstVisible { get; set; }
    public float AngularErrorAtFirstShot { get; set; }
    public float PeakOvershootDegrees { get; set; }
    public float CorrectionCount { get; set; }
    public float FlickDistanceDegrees { get; set; }
    public float SpeedRatioAtFirstShot { get; set; }
    public float ShotsWhileMovingRatio { get; set; }
    public float DistanceToEnemy { get; set; }

    // Ordinal code; trees split it well enough. Switch to one-hot if a linear trainer is used.
    public float WeaponClass { get; set; }
    public float IsKill { get; set; }
    public float PlayerHealth { get; set; }
    public float OpponentHealth { get; set; }
    public float OpponentWeaponClass { get; set; }
    public float FiredFirst { get; set; }

    public bool Label { get; set; }
    public float Weight { get; set; } = 1f;
    public string DemoHash { get; set; } = "";

    public MlEncounterRow Clone() => (MlEncounterRow)MemberwiseClone();

    public static MlEncounterRow From(EncounterFeatures f, bool label = false, float weight = 1f, string demoHash = "") => new()
    {
        TimeToFirstShot = f.TimeToFirstShot,
        TimeToOnTarget = f.TimeToOnTarget ?? NeverOnTargetSeconds,
        ReachedTarget = f.TimeToOnTarget is null ? 0f : 1f,
        AngularErrorAtFirstVisible = f.AngularErrorAtFirstVisible,
        AngularErrorAtFirstShot = f.AngularErrorAtFirstShot,
        PeakOvershootDegrees = f.PeakOvershootDegrees,
        CorrectionCount = f.CorrectionCount,
        FlickDistanceDegrees = f.FlickDistanceDegrees,
        SpeedRatioAtFirstShot = f.SpeedRatioAtFirstShot,
        ShotsWhileMovingRatio = f.ShotsWhileMovingRatio,
        DistanceToEnemy = f.DistanceToEnemy,
        WeaponClass = (float)f.WeaponClass,
        IsKill = f.IsKill ? 1f : 0f,
        PlayerHealth = f.PlayerHealth,
        OpponentHealth = f.OpponentHealth,
        OpponentWeaponClass = (float)f.OpponentWeaponClass,
        FiredFirst = f.FiredFirst ? 1f : 0f,
        Label = label,
        Weight = weight,
        DemoHash = demoHash,
    };
}

/// <summary>ML.NET output schema of one category model.</summary>
public sealed class MlPredictionRow
{
    public float Probability { get; set; }
    public float[] FeatureContributions { get; set; } = [];
}
