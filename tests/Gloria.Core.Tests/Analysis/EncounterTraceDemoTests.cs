using Gloria.Core.Analysis;
using Gloria.Core.Encounters;
using Gloria.Core.Tests.Fixtures;

namespace Gloria.Core.Tests.Analysis;

[Trait("Category", DemoFiles.Category)]
public class EncounterTraceDemoTests
{
    [Fact]
    public async Task KillTraces_ShowTheDamageThatTookTheOpponentsHealth()
    {
        var demo = await DemoFiles.ParseFirstOrSkipAsync();
        var output = TestContext.Current.TestOutputHelper!;
        var index = DemoIndex.For(demo);
        var kills = new EncounterSegmenter().SegmentAll(demo).Encounters.Where(e => e.Outcome == EncounterOutcome.Kill).ToList();
        var checkedKills = 0;

        foreach (var encounter in kills)
        {
            var trace = EncounterTraceBuilder.Build(demo, encounter)!;
            var scene = trace.Scene!;
            Assert.True(scene.Death is { ByPlayer: true }, $"No kill in {encounter}");
            Assert.Equal(encounter.EndTick, scene.Death!.Value.Tick);
            Assert.NotEqual(0f, scene.PlayerX.Max() - scene.PlayerX.Min() + scene.PlayerY.Max() - scene.PlayerY.Min() + 1);
            Assert.NotEmpty(scene.PlayerWeapons);

            // The health series ends at 0. dmg_health is not exact (18 damage can kill at 19 HP), so don't sum it against health.
            Assert.Equal(0, scene.OpponentHealth[^1]);
            var hurts = index.HurtsOf(encounter.OpponentSteamId).Where(h => h.Tick >= trace.StartTick && h.Tick <= encounter.EndTick);
            Assert.Equal(hurts.Where(h => h.AttackerSteamId == encounter.PlayerSteamId).Sum(h => h.DamageHealth), scene.Damage.Where(d => d.ByPlayer).Sum(d => d.Damage));
            // Damage names the variant fired (m4a1_silencer), not the hurt event's base item (m4a1).
            foreach (var shot in trace.Shots.Where(s => s.Hit))
                Assert.Contains(scene.Damage, d => d.ByPlayer && d.Weapon == shot.Weapon);
            checkedKills++;
        }

        var sample = EncounterTraceBuilder.Build(demo, kills[0])!.Scene!;
        output.WriteLine($"{checkedKills} kills; first: health {string.Join(",", sample.OpponentHealth.Distinct())}, " +
                         $"weapons {string.Join(",", sample.PlayerWeapons)} vs {string.Join(",", sample.OpponentWeapons)}, " +
                         $"damage {string.Join(",", sample.Damage)}, death {sample.Death}");
        Assert.True(checkedKills > 50);
    }
}
