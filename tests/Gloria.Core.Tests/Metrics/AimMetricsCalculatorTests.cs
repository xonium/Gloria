using System.Numerics;
using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;
using Gloria.Core.Tests.Fixtures;
using static Gloria.Core.Tests.Fixtures.Scenarios;

namespace Gloria.Core.Tests.Metrics;

public class AimMetricsCalculatorTests
{
    private readonly EncounterSegmenter _segmenter = new();
    private readonly AimMetricsCalculator _calculator = new();

    private EncounterMetrics MetricsFor(ParsedDemo demo)
    {
        var encounter = Assert.Single(_segmenter.Segment(demo, A));
        return Assert.IsType<EncounterMetrics>(_calculator.Calculate(demo, encounter));
    }

    [Fact]
    public void CleanFlick_HasExactTimingAndNoOvershootOrCorrections()
    {
        var demo = Duel()
            .AimPath(A, B, (100, 20), (110, 0))
            .Spotted(B, by: A, 100, 130)
            .Fire(112, A).Hurt(112, B, A).Death(112, B, A)
            .Build(300);

        var m = MetricsFor(demo);

        Assert.Equal(12 / 64.0, m.TimeToFirstShot!.Value, 6);
        Assert.Equal(AimApproach.OnTarget, m.Approach);
        Assert.Equal(0f, m.PeakOvershootDegrees);
        Assert.Equal(0, m.CorrectionCount);
        Assert.InRange(m.AngularErrorAtStart, 19.5f, 20.5f);
        Assert.InRange(m.FlickDistanceDegrees, 19.5f, 20.5f);
        Assert.True(m.AngularErrorAtFirstShot < 0.1f);
        Assert.InRange(m.DistanceAtFirstShot, 990f, 1010f);
    }

    [Fact]
    public void CrosshairThatGoesPastTheTargetAndComesBack_IsAnOvershoot()
    {
        var demo = Duel()
            .AimPath(A, B, (100, 10), (110, -3), (113, -3), (116, 0))
            .Spotted(B, by: A, 100, 130)
            .Fire(118, A)
            .Build(300);

        var m = MetricsFor(demo);

        Assert.Equal(AimApproach.Overshoot, m.Approach);
        Assert.InRange(m.PeakOvershootDegrees, 2.8f, 3.1f);
    }

    [Fact]
    public void CrosshairThatStopsShortTwice_IsAnUndershoot()
    {
        var demo = Duel()
            .AimPath(A, B, (100, 10), (104, 6), (107, 6), (110, 3), (113, 3), (116, 0.5f))
            .Spotted(B, by: A, 100, 130)
            .Fire(118, A)
            .Build(300);

        var m = MetricsFor(demo);

        Assert.Equal(0f, m.PeakOvershootDegrees);
        Assert.Equal(2, m.CorrectionCount);
        Assert.Equal(AimApproach.Undershoot, m.Approach);
    }

    [Fact]
    public void ShotWhileRunning_HasASpeedRatioAboveOne()
    {
        var demo = Duel()
            .Animate(A, 0, 300, _ => new SyntheticDemoBuilder.Pose(Velocity: new Vector3(200, 0, 0)))
            .Spotted(B, by: A, 100, 130)
            .Fire(105, A)
            .Build(300);

        var m = MetricsFor(demo);

        Assert.InRange(m.SpeedRatioAtFirstShot, 2.6f, 2.9f); // 200 / (215 * 0.34)
        Assert.Equal(1, m.ShotsWhileMoving);
    }

    [Fact]
    public void ScopedAwp_UsesTheScopedSpeedThreshold()
    {
        var demo = Duel()
            .Animate(A, 0, 300, _ => new SyntheticDemoBuilder.Pose(Velocity: new Vector3(50, 0, 0), Weapon: "weapon_awp"))
            .Spotted(B, by: A, 100, 130)
            .Fire(105, A, "weapon_awp")
            .Build(300);
        var scoped = demo with { Timeline = Rescope(demo) };

        var unscopedRatio = MetricsFor(demo).SpeedRatioAtFirstShot;
        var scopedRatio = MetricsFor(scoped).SpeedRatioAtFirstShot;

        Assert.True(unscopedRatio < 1f);
        Assert.True(scopedRatio > 1f);
    }

    [Fact]
    public void PreAimedShot_HasNoApproach()
    {
        var demo = Duel()
            .AimPath(A, B, (0, 0.5f))
            .Spotted(B, by: A, 100, 130)
            .Fire(120, A)
            .Build(300);

        var m = MetricsFor(demo);

        Assert.Equal(120, m.ApproachStartTick);
        Assert.Equal(0f, m.FlickDistanceDegrees, 3);
        Assert.Equal(0.0, m.TimeToOnTarget);
    }

    private static PlayerTimeline Rescope(ParsedDemo demo)
    {
        var builder = new PlayerTimelineBuilder();
        foreach (var tick in demo.Timeline.RecordedTicks)
        {
            builder.BeginTick(tick);
            foreach (var id in demo.Timeline.SteamIds)
            {
                demo.Timeline.TryGet(id, tick, out var s);
                builder.Set(id == A ? s with { IsScoped = true } : s);
            }
        }

        return builder.Build();
    }
}
