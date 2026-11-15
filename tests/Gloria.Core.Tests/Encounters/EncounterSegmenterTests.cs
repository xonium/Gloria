using System.Numerics;
using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Tests.Fixtures;
using static Gloria.Core.Tests.Fixtures.Scenarios;

namespace Gloria.Core.Tests.Encounters;

public class EncounterSegmenterTests
{
    private readonly EncounterSegmenter _segmenter = new();

    [Fact]
    public void Kill_StartsAtFirstVisibilityAndEndsAtTheDeath()
    {
        var demo = Duel()
            .AimPath(A, B, (100, 20), (110, 0))
            .Spotted(B, by: A, 100, 130)
            .Fire(112, A).Hurt(112, B, A).Death(112, B, A)
            .Build(300);

        var encounter = Assert.Single(_segmenter.Segment(demo, A));

        Assert.Equal(new Encounter(1, A, B, 100, 112, EncounterOutcome.Kill), encounter);
    }

    [Fact]
    public void VictimWhoNeverFired_IsDroppedAndCountedAsADeathWithoutShots()
    {
        var demo = Duel()
            .Spotted(B, by: A, 100, 130)
            .Spotted(A, by: B, 100, 130)
            .Fire(112, A).Hurt(112, B, A).Death(112, B, A)
            .Build(300);

        var result = _segmenter.SegmentWithStats(demo, B);

        Assert.Empty(result.Encounters);
        Assert.Equal(1, result.DroppedWithoutShots);
        Assert.Equal(1, result.DroppedDeathsWithoutShots);
    }

    [Fact]
    public void ShotsAtAnOpponentWhoWasNeverSeen_AreDroppedAsUnseen()
    {
        // B spots A, so the demo has spotted data and the view-cone fallback is not used.
        var demo = Duel()
            .AimPath(A, B, (0, 0))
            .Spotted(A, by: B, 100, 130)
            .Fire(110, A).Fire(120, A)
            .Build(300);

        var result = _segmenter.SegmentWithStats(demo, A);

        Assert.Empty(result.Encounters);
        Assert.Equal(1, result.DroppedUnseen);
    }

    [Fact]
    public void SpottedShortlyAfterTheShot_StillCountsAsSeen()
    {
        // The spotted state updates every ~33 ticks, so it can turn on after the first shot.
        var demo = Duel()
            .Spotted(B, by: A, 140, 170)
            .Fire(110, A)
            .Build(300);

        Assert.Single(_segmenter.Segment(demo, A));
    }

    [Fact]
    public void ContactGapLongerThanTheDisengageTime_SplitsTheEncounter()
    {
        var demo = Duel()
            .Spotted(B, by: A, 100, 150)
            .Spotted(B, by: A, 400, 450)
            .Fire(120, A).Fire(420, A)
            .Build(600);

        var encounters = _segmenter.Segment(demo, A);

        Assert.Equal(2, encounters.Count);
        Assert.Equal((100, 150, EncounterOutcome.Disengage), (encounters[0].StartTick, encounters[0].EndTick, encounters[0].Outcome));
        Assert.Equal(400, encounters[1].StartTick);
    }

    [Fact]
    public void ContactRunningIntoTheRoundEnd_IsATimeout()
    {
        var demo = Duel(roundEnd: 500)
            .Spotted(B, by: A, 400, 450)
            .Fire(420, A)
            .Build(600);

        var encounter = Assert.Single(_segmenter.Segment(demo, A));

        Assert.Equal(EncounterOutcome.Timeout, encounter.Outcome);
    }

    [Fact]
    public void OpponentKilledByAThirdPlayer_IsADisengage()
    {
        var demo = Duel()
            .Player(C, Team.Terrorist, new Vector3(0, 500, 0))
            .Spotted(B, by: A, 100, 130)
            .Fire(110, A)
            .Death(130, B, C)
            .Build(300);

        var encounter = Assert.Single(_segmenter.Segment(demo, A));

        Assert.Equal(EncounterOutcome.Disengage, encounter.Outcome);
    }

    [Fact]
    public void ShotWithoutDamage_IsAttributedToTheOpponentNearestTheCrosshair()
    {
        var demo = Duel()
            .Player(C, Team.CounterTerrorist, new Vector3(1000, 150, 0))
            .AimPath(A, C, (0, 0))
            .Fire(105, A)
            .Build(200);

        var shot = Assert.Single(DemoIndex.For(demo).ShotsBy(A));

        Assert.Equal(C, shot.Target);
        Assert.Null(shot.HitTick);
    }

    [Fact]
    public void ShotThatDealtDamage_IsAttributedToTheVictimEvenIfAnotherOpponentIsNearer()
    {
        var demo = Duel()
            .Player(C, Team.CounterTerrorist, new Vector3(1000, 150, 0))
            .AimPath(A, C, (0, 0))
            .Fire(105, A).Hurt(106, B, A)
            .Build(200);

        var shot = Assert.Single(DemoIndex.For(demo).ShotsBy(A));

        Assert.Equal(B, shot.Target);
        Assert.Equal(106, shot.HitTick);
    }

    [Fact]
    public void KnifeAndGrenadeThrows_AreNotShots()
    {
        var demo = Duel()
            .Fire(105, A, "weapon_knife").Fire(110, A, "weapon_hegrenade")
            .Build(200);

        Assert.Empty(DemoIndex.For(demo).ShotsBy(A));
    }
}
