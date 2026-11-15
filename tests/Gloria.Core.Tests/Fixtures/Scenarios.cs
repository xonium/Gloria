using System.Numerics;
using Gloria.Core.Ingestion;

namespace Gloria.Core.Tests.Fixtures;

/// <summary>Shared synthetic duel setups: player A (T) at the origin facing B (CT) 1000 units down +X.</summary>
public static class Scenarios
{
    public const ulong A = 1;
    public const ulong B = 2;
    public const ulong C = 3;

    public static SyntheticDemoBuilder Duel(int roundEnd = 3000) => new SyntheticDemoBuilder()
        .Player(A, Team.Terrorist, Vector3.Zero)
        .Player(B, Team.CounterTerrorist, new Vector3(1000, 0, 0))
        .Round(0, 64, roundEnd);

    /// <summary>A's yaw offset from B follows piecewise-linear keyframes (tick, degrees) from the first keyframe on.</summary>
    public static SyntheticDemoBuilder AimPath(this SyntheticDemoBuilder demo, ulong player, ulong target, params (int Tick, float Offset)[] keys)
    {
        demo.Animate(player, 0, int.MaxValue, tick =>
        {
            var offset = Interpolate(keys, tick);
            var (yaw, pitch) = demo.AimAt(player, target, offset);
            return new SyntheticDemoBuilder.Pose(Yaw: yaw, Pitch: pitch);
        });
        return demo;
    }

    private static float Interpolate((int Tick, float Offset)[] keys, int tick)
    {
        if (tick <= keys[0].Tick)
            return keys[0].Offset;

        for (var i = 1; i < keys.Length; i++)
        {
            if (tick <= keys[i].Tick)
            {
                var (t0, o0) = keys[i - 1];
                var (t1, o1) = keys[i];
                return o0 + (o1 - o0) * (tick - t0) / (float)(t1 - t0);
            }
        }

        return keys[^1].Offset;
    }
}
