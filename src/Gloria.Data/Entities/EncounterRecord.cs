using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Metrics;
using Gloria.Core.Weapons;

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

    /// <summary>Where the view movement that led to the first shot began.</summary>
    public int ApproachStartTick { get; set; }

    /// <summary>
    /// The crosshair's error to the body at <see cref="ApproachStartTick"/> (the flick the player needed).
    /// Null for encounters analyzed before it was measured.
    /// </summary>
    public float? PreAimErrorDegrees { get; set; }

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

    public float SmallFlickProbability { get; set; }

    public float MediumFlickProbability { get; set; }

    public float LargeFlickProbability { get; set; }

    /// <summary>The encoded <see cref="Core.Analysis.EncounterTrace"/>; see <see cref="Import.TraceCodec"/>.</summary>
    public byte[]? Trace { get; set; }

    public DemoRecord? Demo { get; set; }

    public bool Has(AimCategory category) => (Flags & (1 << (int)category)) != 0;

    /// <summary>Whether the classifier flagged the category, before any correction.</summary>
    public bool ModelFlagged(AimCategory category) => Probability(category) >= 0.5f;

    /// <summary>Stores the classifier's scores, then the flags with <paramref name="corrections"/> applied.</summary>
    public void ApplyClassification(AimClassification classification, string source, IReadOnlyDictionary<AimCategory, bool>? corrections = null)
    {
        ClassifierSource = source;
        OvershootProbability = classification[AimCategory.Overshoot].Probability;
        UndershootProbability = classification[AimCategory.Undershoot].Probability;
        MovingShotProbability = classification[AimCategory.MovingShot].Probability;
        HastyProbability = classification[AimCategory.Hasty].Probability;
        SlowReactionProbability = classification[AimCategory.SlowReaction].Probability;
        SmallFlickProbability = classification[AimCategory.SmallFlick].Probability;
        MediumFlickProbability = classification[AimCategory.MediumFlick].Probability;
        LargeFlickProbability = classification[AimCategory.LargeFlick].Probability;
        ApplyCorrections(corrections);
    }

    /// <summary>
    /// Sets <see cref="Flags"/> and <see cref="PrimaryCategory"/> from the classifier's scores, with the user's
    /// verdicts taking precedence.
    /// </summary>
    public void ApplyCorrections(IReadOnlyDictionary<AimCategory, bool>? corrections)
    {
        var flags = 0;
        foreach (var category in Enum.GetValues<AimCategory>())
        {
            var flagged = corrections is not null && corrections.TryGetValue(category, out var corrected) ? corrected : ModelFlagged(category);
            if (flagged)
                flags |= 1 << (int)category;
        }

        Flags = flags;
        PrimaryCategory = Enum.GetValues<AimCategory>()
            .Where(Has)
            .OrderByDescending(Probability)
            .Select(c => (AimCategory?)c)
            .FirstOrDefault();
    }

    /// <summary>
    /// The features this encounter was classified from, rebuilt from the stored metrics, so models can be run
    /// on it without the demo. Matches <see cref="EncounterFeatureExtractor"/>, except that the moving-shot ratio
    /// counts every attributed shot rather than only those with a recorded speed. Encounters analyzed before the
    /// pre-aim error was measured use the error at first contact in its place.
    /// </summary>
    public EncounterFeatures ToFeatures(IWeaponData? weapons = null)
    {
        weapons ??= StaticWeaponData.Instance;
        return new EncounterFeatures(
            TimeToFirstShot: (float)(TimeToFirstShot ?? 0),
            TimeToOnTarget: TimeToOnTarget is { } t ? (float)t : null,
            AngularErrorAtFirstVisible: AngularErrorAtStart,
            AngularErrorAtFirstShot: AngularErrorAtFirstShot,
            PeakOvershootDegrees: PeakOvershootDegrees,
            CorrectionCount: CorrectionCount,
            FlickDistanceDegrees: FlickDistanceDegrees,
            SpeedRatioAtFirstShot: SpeedRatioAtFirstShot,
            ShotsWhileMovingRatio: ShotCount > 0 ? ShotsWhileMoving / (float)ShotCount : 0f,
            DistanceToEnemy: DistanceAtFirstShot,
            WeaponClass: weapons.Classify(FirstShotWeapon),
            IsKill: Outcome == EncounterOutcome.Kill,
            PlayerHealth: PlayerHealthAtStart,
            OpponentHealth: OpponentHealthAtStart,
            OpponentWeaponClass: weapons.Classify(OpponentWeapon),
            FiredFirst: FiredFirst,
            PreAimErrorDegrees: PreAimErrorDegrees ?? AngularErrorAtStart);
    }

    public float Probability(AimCategory category) => category switch
    {
        AimCategory.Overshoot => OvershootProbability,
        AimCategory.Undershoot => UndershootProbability,
        AimCategory.MovingShot => MovingShotProbability,
        AimCategory.Hasty => HastyProbability,
        AimCategory.SlowReaction => SlowReactionProbability,
        AimCategory.SmallFlick => SmallFlickProbability,
        AimCategory.MediumFlick => MediumFlickProbability,
        AimCategory.LargeFlick => LargeFlickProbability,
        _ => 0f,
    };
}
