using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Sprays;
using Gloria.Core.Tests.Fixtures;
using Gloria.Core.Weapons;

namespace Gloria.Core.Tests.Sprays;

/// <summary>Checks the shot counter that sprays are cut by, so a parser update that breaks it fails here.</summary>
[Trait("Category", DemoFiles.Category)]
public class SprayDemoTests
{
    [Fact]
    public async Task TheShotCounter_ClimbsWithinASpray_AndReadsOneAfterAPause()
    {
        var demo = await DemoFiles.ParseFirstOrSkipAsync();
        var index = DemoIndex.For(demo);
        int quick = 0, climbing = 0, afterPause = 0, atOne = 0, total = 0, known = 0;

        foreach (var shooter in demo.Timeline.SteamIds)
        {
            WeaponFireEvent? previous = null;
            foreach (var fire in index.FiresBy(shooter))
            {
                total++;
                known += fire.ShotsFired is null ? 0 : 1;
                if (!SprayWeapons.TryGetClip(fire.Weapon, out _))
                {
                    previous = null;
                    continue;
                }

                if (previous is not null && StaticWeaponData.Normalize(previous.Weapon) == StaticWeaponData.Normalize(fire.Weapon))
                {
                    // Full auto fires every 4 to 7 ticks: most shots that soon come from fire held down.
                    var gap = fire.Tick - previous.Tick;
                    if (gap <= 7)
                    {
                        quick++;
                        climbing += fire.ShotsFired == previous.ShotsFired + 1 ? 1 : 0;
                    }
                    else if (gap > demo.TickRate)
                    {
                        afterPause++;
                        atOne += fire.ShotsFired == 1 ? 1 : 0;
                    }
                }

                previous = fire;
            }
        }

        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{total} fires, {known} with the counter; {climbing} of {quick} quick follow-ups climb by one; {atOne} of {afterPause} shots after a pause read 1");
        Assert.True(known >= 0.99 * total, $"Only {known} of {total} fires have the counter.");
        Assert.True(quick >= 100 && climbing >= 0.9 * quick, $"Only {climbing} of {quick} quick follow-ups climb by one.");
        Assert.True(afterPause >= 20 && atOne >= 0.95 * afterPause, $"Only {atOne} of {afterPause} shots after a pause read 1.");
    }

    [Fact]
    public async Task Sprays_StayWithinTheMagazine_AndThinOut()
    {
        var demo = await DemoFiles.ParseFirstOrSkipAsync();
        var sprays = SprayAnalyzer.Compute(demo);

        Assert.NotEmpty(sprays);
        foreach (var gun in sprays.GroupBy(s => (s.SteamId, s.Weapon)))
        {
            Assert.True(SprayWeapons.TryGetClip(gun.Key.Weapon, out var clip));
            Assert.All(gun, s => Assert.InRange(s.Bullet, 1, clip));
            Assert.All(gun, s => Assert.InRange(s.Hits, s.Kills, s.Fired));

            // A bullet is never reached by more sprays than the one before it, except the last, where overlong runs pile up.
            var fired = gun.Where(s => s.Bullet < clip).OrderBy(s => s.Bullet).Select(s => s.Fired).ToList();
            Assert.Equal(fired.OrderDescending(), fired);
        }
    }
}
