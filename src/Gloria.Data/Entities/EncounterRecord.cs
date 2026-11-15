using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Metrics;

namespace Gloria.Data.Entities;

/// <summary>
/// One encounter of one player, with the metrics, classification and trace the UI shows.
/// Demos are analyzed once at import for every player in them, so these exist for all ten players.
/// </summary>
public sealed class EncounterRecord
{
    public int Id { get; set; }

    public int DemoId { get; set; }

    public ulong PlayerSteamId { get; set; }

    public ulong OpponentSteamId { get; set; }

    public int RoundNumber { get; set; }

    public int StartTick { get; set; }

    public int EndTick { get; set; }

    public EncounterOutcome Outcome { get; set; }

    public AimApproach Approach { get; set; }

    public double? TimeToFirstShot { get; set; }

    public double? TimeToOnTarget { get; set; }

    public int FirstShotTick { get; set; }

    public string FirstShotWeapon { get; set; } = "";

    public string OpponentWeapon { get; set; } = "";

    public int ShotCount { get; set; }

    public int ShotsWhileMoving { get; set; }

    public float SpeedAtFirstShot { get; set; }

    public float SpeedRatioAtFirstShot { get; set; }

    public float AngularErrorAtStart { get; set; }

    public float AngularErrorAtFirstShot { get; set; }

    public float PeakOvershootDegrees { get; set; }

    public int CorrectionCount { get; set; }

    public float FlickDistanceDegrees { get; set; }

    public float DistanceAtFirstShot { get; set; }

    public int PlayerHealthAtStart { get; set; }

    public int OpponentHealthAtStart { get; set; }

    public bool FiredFirst { get; set; }

    public bool? FirstShotHit { get; set; }

    public bool? WonDuel { get; set; }

    /// <summary>Flagged <see cref="AimCategory"/> values as a bit mask of <c>1 &lt;&lt; category</c>.</summary>
    public int Flags { get; set; }

    public AimCategory? PrimaryCategory { get; set; }

    /// <summary><c>IAimClassifier.Source</c> that produced the flags, e.g. "rules" or a model version.</summary>
    public string ClassifierSource { get; set; } = "";

    public float OvershootProbability { get; set; }

    public float UndershootProbability { get; set; }

    public float MovingShotProbability { get; set; }

    public float HastyProbability { get; set; }

    public float SlowReactionProbability { get; set; }

    /// <summary>The encoded <see cref="Core.Analysis.EncounterTrace"/>; see <see cref="Import.TraceCodec"/>.</summary>
    public byte[]? Trace { get; set; }

    public DemoRecord? Demo { get; set; }

    public bool Has(AimCategory category) => (Flags & (1 << (int)category)) != 0;

    public float Probability(AimCategory category) => category switch
    {
        AimCategory.Overshoot => OvershootProbability,
        AimCategory.Undershoot => UndershootProbability,
        AimCategory.MovingShot => MovingShotProbability,
        AimCategory.Hasty => HastyProbability,
        AimCategory.SlowReaction => SlowReactionProbability,
        _ => 0f,
    };
}
