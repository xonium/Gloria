using System.Numerics;
using Gloria.Core.Ingestion;
using Gloria.Core.Sprays;
using Gloria.Core.Tests.Fixtures;

namespace Gloria.Core.Tests.Sprays;

public class SprayAnalyzerTests
{
    private const ulong A = 1;
    private const ulong B = 2;
    private const ulong C = 3;

    // An AK fires every 6.4 ticks at 64 ticks a second.
    private const int Cycle = 6;

    // One live round from tick 100 to 3000.
    private static SyntheticDemoBuilder Match() => new SyntheticDemoBuilder()
        .Player(A, Team.Terrorist, Vector3.Zero)
        .Player(B, Team.CounterTerrorist, new Vector3(500, 0, 0))
        .Player(C, Team.CounterTerrorist, new Vector3(500, 40, 0))
        .Round(100, 200, 3000);

    /// <summary>Fires <paramref name="count"/> shots from <paramref name="start"/>, the counter climbing from 1.</summary>
    private static SyntheticDemoBuilder Spray(SyntheticDemoBuilder demo, int start, int count, string weapon = "weapon_ak47")
    {
        for (var i = 0; i < count; i++)
            demo.Fire(start + i * Cycle, A, weapon, i + 1);
        return demo;
    }

    private static Dictionary<int, SprayBulletStats> Bullets(ParsedDemo demo, string weapon = "ak47") =>
        SprayAnalyzer.Compute(demo).Where(s => s.SteamId == A && s.Weapon == weapon).ToDictionary(s => s.Bullet);

    [Fact]
    public void AFiveBulletBurst_FillsBulletsOneToFiveOnly()
    {
        var bullets = Bullets(Spray(Match(), 300, 5).Build(3100));

        Assert.Equal([1, 2, 3, 4, 5], bullets.Keys.Order());
        Assert.All(bullets.Values, b => Assert.Equal(1, b.Fired));
    }

    [Fact]
    public void ACounterReset_StartsANewSpray_EvenRightAfterTheLastShot()
    {
        // Two taps one cycle apart: the counter reads 1 both times, so fire was released in between.
        var demo = Match()
            .Fire(300, A, shotsFired: 1)
            .Fire(300 + Cycle, A, shotsFired: 1)
            .Build(3100);

        var bullets = Bullets(demo);

        Assert.Equal(2, bullets[1].Fired);
        Assert.False(bullets.ContainsKey(2));
    }

    [Fact]
    public void ABurstStartedMidMagazine_CountsFromOne()
    {
        var demo = Spray(Spray(Match(), 300, 10), 600, 5).Build(3100);

        var bullets = Bullets(demo);

        Assert.Equal(2, bullets[1].Fired);
        Assert.Equal(2, bullets[5].Fired);
        Assert.Equal(1, bullets[6].Fired);
        Assert.Equal(1, bullets[10].Fired);
    }

    [Fact]
    public void AWeaponSwitch_OrALongPause_StartsANewSpray()
    {
        var demo = Match()
            .Fire(300, A, "weapon_ak47", 1)
            .Fire(300 + Cycle, A, "weapon_m4a1", 2)
            // Fire held through a reload: the counter keeps climbing, but this is a new spray.
            .Fire(300 + Cycle + 200, A, "weapon_m4a1", 3)
            .Build(3100);

        var all = SprayAnalyzer.Compute(demo).Where(s => s.SteamId == A).ToList();

        Assert.Equal(1, all.Single(s => s.Weapon == "ak47").Fired);
        Assert.Equal(2, all.Single(s => s is { Weapon: "m4a1", Bullet: 1 }).Fired);
        Assert.DoesNotContain(all, s => s.Bullet > 1);
    }

    [Fact]
    public void WithoutTheCounter_AGapEndsTheSpray()
    {
        var demo = Match()
            .Fire(300, A)
            .Fire(300 + Cycle, A)
            .Fire(300 + Cycle + 30, A)
            .Build(3100);

        var bullets = Bullets(demo);

        Assert.Equal(2, bullets[1].Fired);
        Assert.Equal(1, bullets[2].Fired);
    }

    [Fact]
    public void HitsAndKills_AreCountedPerBullet_AndABulletThroughTwoPlayersIsOneHit()
    {
        const int start = 300;
        var demo = Spray(Match(), start, 4)
            // Bullet 2 goes through B into C.
            .Hurt(start + Cycle, B, A, healthAfter: 73)
            .Hurt(start + Cycle, C, A, healthAfter: 80)
            // Bullet 4 kills B.
            .Hurt(start + 3 * Cycle, B, A, damage: 111, healthAfter: 0)
            .Death(start + 3 * Cycle, B, A)
            .Build(3100);

        var bullets = Bullets(demo);

        Assert.Equal(0, bullets[1].Hits);
        Assert.Equal((1, 1, 0), (bullets[2].Fired, bullets[2].Hits, bullets[2].Kills));
        Assert.Equal(0, bullets[3].Hits);
        Assert.Equal((1, 1), (bullets[4].Hits, bullets[4].Kills));
    }

    [Fact]
    public void AnM4A1SHit_CountsEvenThoughTheHurtEventNamesTheM4A4()
    {
        var demo = Spray(Match(), 300, 1, "weapon_m4a1_silencer")
            .Hurt(300, B, A, "m4a1", damage: 120, healthAfter: 0)
            .Death(300, B, A, "m4a1_silencer")
            .Build(3100);

        var bullet = Bullets(demo, "m4a1_silencer")[1];

        Assert.Equal((1, 1), (bullet.Hits, bullet.Kills));
    }

    [Fact]
    public void SemiAutoGuns_AndShotsOutsideLiveRounds_AreLeftOut()
    {
        var demo = Match()
            .Fire(300, A, "weapon_awp", 1)
            .Fire(400, A, "weapon_deagle", 1)
            .Fire(500, A, "weapon_xm1014", 1)
            .Fire(50, A, "weapon_ak47", 1)
            .Build(3100);

        Assert.DoesNotContain(SprayAnalyzer.Compute(demo), s => s.SteamId == A);
    }

    [Fact]
    public void ASprayLongerThanTheMagazine_IsCappedAtItsSize()
    {
        // The M4A1-S holds 20; a longer run means a missed reset, and the rest stays on the last bullet.
        var bullets = Bullets(Spray(Match(), 300, 22, "weapon_m4a1_silencer").Build(3100), "m4a1_silencer");

        Assert.Equal(20, bullets.Keys.Max());
        Assert.Equal(3, bullets[20].Fired);
    }
}
