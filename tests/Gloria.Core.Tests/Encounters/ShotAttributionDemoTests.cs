using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Tests.Fixtures;
using Gloria.Core.Weapons;

namespace Gloria.Core.Tests.Encounters;

[Trait("Category", DemoFiles.Category)]
public class ShotAttributionDemoTests
{
    /// <summary>
    /// Hurt events name the base item (m4a1 for an M4A1-S, hkp2000 for a USP-S). Matching them by the fired
    /// variant's name once turned every M4A1-S and USP-S hit into a miss, and kills into "0 hit".
    /// </summary>
    [Fact]
    public async Task EveryGunKill_HasAHit()
    {
        var demo = await DemoFiles.ParseFirstOrSkipAsync();
        var index = DemoIndex.For(demo);
        var kills = new EncounterSegmenter().SegmentAll(demo).Encounters.Where(e => e.Outcome == EncounterOutcome.Kill).ToList();
        var withoutHit = kills
            .Where(e => StaticWeaponData.Instance.Classify(index.DeathOf(e.OpponentSteamId, e.StartTick, e.EndTick)!.Weapon) != WeaponClass.Other)
            .Where(e => !index.ShotsBy(e.PlayerSteamId).Any(s =>
                s.Target == e.OpponentSteamId && s.HitTick is not null && s.Tick >= e.StartTick && s.Tick <= e.EndTick))
            .ToList();

        Assert.True(kills.Count > 50, $"Only {kills.Count} kills.");
        Assert.Empty(withoutHit);
    }

    [Fact]
    public async Task GunDamage_IsClaimedByAShot()
    {
        var demo = await DemoFiles.ParseFirstOrSkipAsync();
        var index = DemoIndex.For(demo);
        var output = TestContext.Current.TestOutputHelper!;
        int total = 0, unclaimed = 0;
        foreach (var attacker in demo.Timeline.SteamIds)
        {
            var claimed = index.ShotsBy(attacker).Where(s => s.HitTick is not null).Select(s => (s.HitTick!.Value, s.Target!.Value)).ToHashSet();
            foreach (var hurt in index.HurtsBy(attacker).Where(h => StaticWeaponData.Instance.Classify(h.Weapon) != WeaponClass.Other))
            {
                total++;
                if (claimed.Contains((hurt.Tick, hurt.VictimSteamId)))
                    continue;

                unclaimed++;
                var fire = index.FiresBy(attacker).MinBy(f => Math.Abs(f.Tick - hurt.Tick));
                output.WriteLine($"Unclaimed: {hurt.Weapon} -{hurt.DamageHealth} at {hurt.Tick}, nearest fire {fire?.Weapon} at {fire?.Tick}");
            }
        }

        output.WriteLine($"{unclaimed} of {total} gun hurts unclaimed");
        Assert.True(total > 200);
        Assert.True(unclaimed <= total / 100, $"{unclaimed} of {total} gun hurts have no shot.");
    }
}
