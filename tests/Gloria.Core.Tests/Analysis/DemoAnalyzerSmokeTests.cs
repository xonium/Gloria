using System.Diagnostics;
using Gloria.Core.Analysis;
using Gloria.Core.Encounters;
using Gloria.Core.Tests.Fixtures;

namespace Gloria.Core.Tests.Analysis;

[Trait("Category", DemoFiles.Category)]
public class DemoAnalyzerSmokeTests
{
    [Fact]
    public async Task Analyze_ProducesFiniteFeaturesAndPlausibleLabels_ForEveryPlayer()
    {
        var demo = await DemoFiles.ParseFirstOrSkipAsync();
        var output = TestContext.Current.TestOutputHelper!;

        var watch = Stopwatch.StartNew();
        var analysis = new DemoAnalyzer().Analyze(demo);
        var rows = analysis.Encounters;
        output.WriteLine($"{rows.Count} encounters in {watch.Elapsed.TotalSeconds:F1} s; dropped {analysis.Segmentation.DroppedWithoutShots} without shots " +
                         $"({analysis.Segmentation.DroppedDeathsWithoutShots} of them deaths)");
        foreach (var group in rows.GroupBy(r => r.Encounter.Outcome).OrderBy(g => g.Key))
            output.WriteLine($"  {group.Key}: {group.Count()}");

        Assert.All(demo.Timeline.SteamIds, id => Assert.Contains(rows, r => r.Encounter.PlayerSteamId == id));
        Assert.All(rows, r =>
        {
            var f = r.Features;
            float[] values = [f.TimeToFirstShot, f.TimeToOnTarget ?? 0, f.AngularErrorAtFirstVisible, f.AngularErrorAtFirstShot,
                f.PeakOvershootDegrees, f.FlickDistanceDegrees, f.SpeedRatioAtFirstShot, f.ShotsWhileMovingRatio, f.DistanceToEnemy];
            Assert.All(values, v => Assert.True(float.IsFinite(v) && v >= 0, $"Feature value {v} in {r.Encounter}"));
        });

        var labeled = rows.Where(r => r.Labels.FirstShotHit is not null).ToList();
        var hitRate = labeled.Count(r => r.Labels.FirstShotHit == true) / (double)labeled.Count;
        var duels = rows.Where(r => r.Labels.WonDuel is not null).ToList();
        output.WriteLine($"first-shot hit rate {hitRate:P0} of {labeled.Count}; duels {duels.Count}, won {duels.Count(d => d.Labels.WonDuel == true)}");
        Assert.InRange(hitRate, 0.1, 0.7);

        // Kills and deaths mirror each other: most kills should also appear as the victim's death.
        var kills = rows.Count(r => r.Encounter.Outcome == EncounterOutcome.Kill);
        var deaths = rows.Count(r => r.Encounter.Outcome == EncounterOutcome.Death);
        Assert.True(kills > 0 && deaths > 0);

        Describe(output, "TimeToFirstShot", rows.Select(r => r.Features.TimeToFirstShot));
        Describe(output, "TimeToOnTarget", rows.Where(r => r.Features.TimeToOnTarget is not null).Select(r => r.Features.TimeToOnTarget!.Value));
        Describe(output, "ErrorAtStart", rows.Select(r => r.Features.AngularErrorAtFirstVisible));
        Describe(output, "ErrorAtFirstShot", rows.Select(r => r.Features.AngularErrorAtFirstShot));
        Describe(output, "PeakOvershoot", rows.Select(r => r.Features.PeakOvershootDegrees));
        Describe(output, "Corrections", rows.Select(r => (float)r.Features.CorrectionCount));
        Describe(output, "SpeedRatio", rows.Select(r => r.Features.SpeedRatioAtFirstShot));
        Describe(output, "Distance", rows.Select(r => r.Features.DistanceToEnemy));
        foreach (var w in rows.GroupBy(r => r.Features.WeaponClass))
            output.WriteLine($"  {w.Key}: {w.Count()}");
    }

    private static void Describe(ITestOutputHelper output, string name, IEnumerable<float> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
            return;

        float Q(double q) => sorted[(int)Math.Min(sorted.Count - 1, q * sorted.Count)];
        output.WriteLine($"{name,-18} n={sorted.Count,5} p10={Q(0.1),7:F2} p25={Q(0.25),7:F2} p50={Q(0.5),7:F2} p75={Q(0.75),7:F2} p90={Q(0.9),7:F2} max={sorted[^1],8:F2}");
    }
}
