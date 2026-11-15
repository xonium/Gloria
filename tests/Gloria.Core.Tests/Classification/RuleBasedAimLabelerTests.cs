using Gloria.Core.Classification;
using Gloria.Core.Weapons;

namespace Gloria.Core.Tests.Classification;

public class RuleBasedAimLabelerTests
{
    // Stopped, on target, quick, no overshoot or corrections: nothing to flag.
    private static readonly EncounterFeatures Clean = new(
        TimeToFirstShot: 0.3f,
        TimeToOnTarget: 0.25f,
        AngularErrorAtFirstVisible: 10f,
        AngularErrorAtFirstShot: 0.5f,
        PeakOvershootDegrees: 0.2f,
        CorrectionCount: 0,
        FlickDistanceDegrees: 10f,
        SpeedRatioAtFirstShot: 0.5f,
        ShotsWhileMovingRatio: 0f,
        DistanceToEnemy: 1000f,
        WeaponClass: WeaponClass.Rifle,
        IsKill: true);

    [Fact]
    public void Label_IsEmpty_ForCleanEncounter()
    {
        Assert.Empty(RuleBasedAimLabeler.Label(Clean));
        Assert.True(RuleBasedAimClassifier.Classify(Clean).IsClean);
    }

    [Fact]
    public void Overshoot_WhenPeakOvershootReachesThreshold()
    {
        var features = Clean with { PeakOvershootDegrees = RuleBasedAimLabeler.OvershootDegrees, CorrectionCount = 3 };

        Assert.Equal([AimCategory.Overshoot], RuleBasedAimLabeler.Label(features));
    }

    [Fact]
    public void Undershoot_WhenSeveralCorrectionsWithoutOvershoot()
    {
        var features = Clean with { CorrectionCount = RuleBasedAimLabeler.UndershootCorrections };

        Assert.Equal([AimCategory.Undershoot], RuleBasedAimLabeler.Label(features));
    }

    [Theory]
    [InlineData(1.0f, false)]
    [InlineData(1.01f, true)]
    public void MovingShot_WhenFasterThanAccurateSpeed(float speedRatio, bool expected)
    {
        var features = Clean with { SpeedRatioAtFirstShot = speedRatio };

        Assert.Equal(expected, RuleBasedAimLabeler.IsFlagged(features, AimCategory.MovingShot));
    }

    [Fact]
    public void Hasty_WhenFirstShotIsOffTarget()
    {
        var features = Clean with { AngularErrorAtFirstShot = 4f, TimeToOnTarget = null };

        Assert.Equal([AimCategory.Hasty], RuleBasedAimLabeler.Label(features));
    }

    [Theory]
    [InlineData(300f, false)]
    [InlineData(2000f, true)]
    public void Hasty_ToleranceShrinksWithDistance(float distance, bool expected)
    {
        var features = Clean with { AngularErrorAtFirstShot = 1.5f, DistanceToEnemy = distance };

        Assert.Equal(expected, RuleBasedAimLabeler.IsFlagged(features, AimCategory.Hasty));
    }

    [Fact]
    public void SlowReaction_WhenFirstShotIsLate()
    {
        var features = Clean with { TimeToFirstShot = 0.9f, TimeToOnTarget = 0.8f };

        Assert.Equal([AimCategory.SlowReaction], RuleBasedAimLabeler.Label(features));
    }

    [Fact]
    public void Primary_IsNull_WhenClean_AndAFlaggedCategoryOtherwise()
    {
        Assert.Null(RuleBasedAimClassifier.Classify(Clean).Primary);

        var moving = RuleBasedAimClassifier.Classify(Clean with { SpeedRatioAtFirstShot = 1.5f });
        Assert.Equal(AimCategory.MovingShot, moving.Primary);
    }
}
