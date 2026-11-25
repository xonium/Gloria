using System.Numerics;
using Gloria.Core.Analysis;
using Gloria.Core.Ingestion;
using Gloria.Core.Tests.Fixtures;

namespace Gloria.Core.Tests.Analysis;

public class PlayerMatchStatsTests
{
    private const ulong A = 1;
    private const ulong Mate = 2;
    private const ulong B = 3;
    private const ulong C = 4;

    // One live round from tick 100 to 1000, then round 2 from 2000 to 3000.
    private static SyntheticDemoBuilder Match() => new SyntheticDemoBuilder()
        .Player(A, Team.Terrorist, Vector3.Zero)
        .Player(Mate, Team.Terrorist, new Vector3(0, 100, 0))
        .Player(B, Team.CounterTerrorist, new Vector3(500, 0, 0))
        .Player(C, Team.CounterTerrorist, new Vector3(500, 100, 0))
        .Round(100, 200, 1000)
        .Round(2000, 2100, 3000);

    [Fact]
    public void CountsEnemyKillsAndHeadshotsAndEveryDeath()
    {
        var demo = Match()
            .Death(300, B, A, headshot: true)
            .Death(400, C, A)
            .Death(500, A, null, "world")
            .Build(3100);

        var stats = PlayerMatchStats.Compute(demo);

        Assert.Equal(new PlayerMatchStats(2, 1, 0, 1), stats[A]);
        Assert.Equal(1, stats[B].Deaths);
        Assert.Equal(1, stats[C].Deaths);
    }

    [Fact]
    public void ATeamKillTakesAKillAwayAndSuicideIsADeathOnly()
    {
        var demo = Match()
            .Death(300, B, A)
            .Death(400, Mate, A)
            .Death(2500, A, A, "hegrenade")
            .Build(3100);

        var stats = PlayerMatchStats.Compute(demo);

        Assert.Equal(0, stats[A].Kills);
        Assert.Equal(1, stats[A].Deaths);
        Assert.Equal(1, stats[Mate].Deaths);
    }

    [Fact]
    public void DamageIsCappedAtTheHealthLeft()
    {
        // 27 + 27 + 115 on 100 HP: the last hit only takes the 46 left.
        var demo = Match()
            .Hurt(300, B, A, damage: 27, healthAfter: 73)
            .Hurt(310, B, A, damage: 27, healthAfter: 46)
            .Hurt(320, B, A, "awp", damage: 115, healthAfter: 0)
            .Death(320, B, A, "awp")
            .Build(3100);

        Assert.Equal(100, PlayerMatchStats.Compute(demo)[A].Damage);
    }

    [Fact]
    public void WithoutRemainingHealthTheReportedDamageIsCapped()
    {
        var demo = Match()
            .Hurt(300, B, A, damage: 80)
            .Hurt(310, B, A, damage: 80)
            .Build(3100);

        Assert.Equal(100, PlayerMatchStats.Compute(demo)[A].Damage);
    }

    [Fact]
    public void RemainingHealthWinsOverTheReportedDamage()
    {
        // The game can report 18 for a hit that kills at 19 HP.
        var demo = Match()
            .Hurt(300, B, A, damage: 81, healthAfter: 19)
            .Hurt(310, B, A, damage: 18, healthAfter: 0)
            .Build(3100);

        Assert.Equal(100, PlayerMatchStats.Compute(demo)[A].Damage);
    }

    [Fact]
    public void HealthResetsEachRound()
    {
        var demo = Match()
            .Hurt(300, B, A, damage: 90, healthAfter: 10)
            .Hurt(2500, B, A, damage: 90, healthAfter: 10)
            .Build(3100);

        Assert.Equal(180, PlayerMatchStats.Compute(demo)[A].Damage);
    }

    [Fact]
    public void TeamAndSelfDamageDoNotCountButStillLowerHealth()
    {
        var demo = Match()
            .Hurt(300, Mate, A, damage: 40, healthAfter: 60)
            .Hurt(310, A, A, "hegrenade", damage: 30, healthAfter: 70)
            .Hurt(400, B, null, "world", damage: 50, healthAfter: 50)
            .Hurt(410, B, A, damage: 100, healthAfter: 0)
            .Build(3100);

        Assert.Equal(50, PlayerMatchStats.Compute(demo)[A].Damage);
    }

    [Fact]
    public void UtilityDamageCounts()
    {
        var demo = Match()
            .Hurt(300, B, A, "hegrenade", damage: 40, healthAfter: 60)
            .Hurt(310, C, A, "inferno", damage: 8, healthAfter: 92)
            .Build(3100);

        Assert.Equal(48, PlayerMatchStats.Compute(demo)[A].Damage);
    }

    [Fact]
    public void CountsAfterTheRoundEndsButNotBeforeTheFirstRound()
    {
        var demo = Match()
            .Death(50, B, A)
            .Hurt(60, C, A, damage: 50, healthAfter: 50)
            .Death(1200, C, A)
            .Build(3100);

        var stats = PlayerMatchStats.Compute(demo);

        Assert.Equal(1, stats[A].Kills);
        Assert.Equal(0, stats[A].Damage);
        Assert.Equal(0, stats[B].Deaths);
    }
}
