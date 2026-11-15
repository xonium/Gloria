using Gloria.Core.Encounters;
using Gloria.Core.Metrics;
using Gloria.Core.Tests.Fixtures;

namespace Gloria.Core.Tests.Metrics;

[Trait("Category", DemoFiles.Category)]
public class RecoilDemoTests
{
    [Fact]
    public async Task HitsInASpray_LandCloserToTheBody_WithRecoil()
    {
        var demo = await DemoFiles.ParseFirstOrSkipAsync();
        var index = DemoIndex.For(demo);
        var without = new List<float>();
        var with = new List<float>();
        var onBodyWithout = 0;
        var onBodyWith = 0;

        foreach (var shooter in demo.Timeline.SteamIds)
        {
            foreach (var shot in index.ShotsBy(shooter).Where(s => s.HitTick is not null && s.Target is not null))
            {
                // The view angles on the tick before the fire tick match where the shot went best.
                if (!demo.Timeline.TryGet(shooter, shot.Tick - 1, out var me)
                    || !demo.Timeline.TryGet(shot.Target!.Value, shot.Tick - 1, out var them)
                    || MathF.Sqrt(me.AimPunchYaw * me.AimPunchYaw + me.AimPunchPitch * me.AimPunchPitch) < 0.3f)
                {
                    continue;
                }

                var tolerance = BodyModel.HalfWidthDegrees(BodyModel.Distance(me, them));
                var crosshair = BodyModel.AngularError(me, them);
                var bullet = BodyModel.AngularError(Recoil.BulletView(me), them);
                without.Add(crosshair);
                with.Add(bullet);
                onBodyWithout += crosshair <= tolerance ? 1 : 0;
                onBodyWith += bullet <= tolerance ? 1 : 0;
            }
        }

        Assert.True(without.Count >= 30, $"Only {without.Count} hits with aim punch.");
        var medianWithout = Median(without);
        var medianWith = Median(with);
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{without.Count} hits with aim punch: median error to the body {medianWithout:0.000}° from the crosshair, {medianWith:0.000}° with recoil; " +
            $"on the body {onBodyWithout / (double)without.Count:P0} vs {onBodyWith / (double)with.Count:P0}");
        Assert.True(medianWith < 0.75f * medianWithout, $"Recoil should bring hits closer: {medianWith:0.000}° vs {medianWithout:0.000}°.");
        Assert.True(onBodyWith > onBodyWithout);
    }

    private static float Median(List<float> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }
}
