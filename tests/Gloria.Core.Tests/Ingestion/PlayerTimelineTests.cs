using System.Numerics;
using Gloria.Core.Ingestion;
using Gloria.Core.Visibility;
using Gloria.Core.Tests.Fixtures;
using static Gloria.Core.Tests.Fixtures.Scenarios;

namespace Gloria.Core.Tests.Ingestion;

public class PlayerTimelineTests
{
    private static PlayerTickState State(int tick, ulong id, float x) =>
        new(tick, id, new Vector3(x, 0, 0), Vector3.Zero, 0, 0, "weapon_ak47", true, Team.Terrorist);

    [Fact]
    public void TickThatWasNotRecorded_ResolvesToTheLatestRecordedTickBeforeIt()
    {
        var builder = new PlayerTimelineBuilder();
        builder.BeginTick(10);
        builder.Set(State(10, A, 1));
        builder.BeginTick(12);
        builder.Set(State(12, A, 2));
        var timeline = builder.Build();

        Assert.True(timeline.TryGet(A, 11, out var state));
        Assert.Equal(10, state.Tick);
        Assert.Equal(1f, state.Position.X);
        Assert.False(timeline.TryGet(A, 9, out _));
        Assert.False(timeline.TryGet(A, 13, out _));
        Assert.Equal("weapon_ak47", state.ActiveWeapon);
    }

    [Fact]
    public void PlayerMissingFromARow_IsNotPresent()
    {
        var builder = new PlayerTimelineBuilder();
        builder.BeginTick(0);
        builder.Set(State(0, A, 1));
        builder.BeginTick(1);
        builder.Set(State(1, B, 1));
        var timeline = builder.Build();

        Assert.False(timeline.TryGet(B, 0, out _));
        Assert.False(timeline.TryGet(A, 1, out _));
    }

    [Fact]
    public void SpottedVisibility_ReadsTheObserversBit()
    {
        var demo = Duel().Spotted(B, by: A, 100, 110).Build(200);

        Assert.True(SpottedVisibilitySource.Instance.CanSee(demo, 105, A, B));
        Assert.False(SpottedVisibilitySource.Instance.CanSee(demo, 105, B, A));
        Assert.False(SpottedVisibilitySource.Instance.CanSee(demo, 111, A, B));
        Assert.True(demo.Timeline.HasSpottedData);
    }

    [Fact]
    public void FieldOfViewFallback_SeesTargetsInFrontOnly()
    {
        var demo = Duel().AimPath(A, B, (0, 0)).Build(10);
        var fov = new FieldOfViewVisibilitySource();

        Assert.True(fov.CanSee(demo, 5, A, B));
        Assert.False(fov.CanSee(demo, 5, B, A)); // B faces +X, away from A
    }

    [Fact]
    public void DerivedVelocity_IsThePositionChangePerSecond()
    {
        var builder = new PlayerTimelineBuilder();
        for (var tick = 0; tick < 3; tick++)
        {
            builder.BeginTick(tick);
            builder.Set(State(tick, A, tick * 2f));
        }

        Assert.False(builder.HasNetworkedVelocity());
        builder.DeriveVelocityFromPositions(64);
        var timeline = builder.Build();

        timeline.TryGet(A, 2, out var state);
        Assert.Equal(128f, state.Velocity.X, 3);
    }
}
