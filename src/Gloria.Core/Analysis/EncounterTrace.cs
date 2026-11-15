using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;

namespace Gloria.Core.Analysis;

/// <summary>A shot inside a trace. Its crosshair position is the sample one tick before <see cref="Tick"/>.</summary>
/// <param name="HitGroup">The game's hit group of the damage it dealt, or null for a miss.</param>
public readonly record struct TraceShot(int Tick, bool Hit, int? HitGroup);

/// <summary>
/// Per-tick samples of one encounter, for charts and the replay. Starts <see cref="EncounterTraceBuilder.LeadSeconds"/>
/// before the encounter (the spotted flag lags) and ends with it.
/// </summary>
/// <param name="ErrorX">Crosshair offset from the opponent's upper chest in degrees, positive to the right on screen.</param>
/// <param name="ErrorY">Crosshair offset from the upper chest in degrees, positive upwards.</param>
/// <param name="BodyError">
/// Angle to the nearest point of the body (0 when on it), negative once the crosshair has passed the body
/// relative to where it started.
/// </param>
/// <param name="Speed">The player's horizontal speed in units per second.</param>
/// <param name="Distance">Eye-to-chest distance in units, for drawing the body to scale.</param>
public sealed record EncounterTrace(
    int StartTick,
    int TickRate,
    float[] ErrorX,
    float[] ErrorY,
    float[] BodyError,
    float[] Speed,
    float[] Distance,
    IReadOnlyList<TraceShot> Shots)
{
    public int Count => ErrorX.Length;

    /// <summary>Seconds of sample <paramref name="index"/> relative to <paramref name="encounterStartTick"/>.</summary>
    public float SecondsAt(int index, int encounterStartTick) => (StartTick + index - encounterStartTick) / (float)TickRate;

    /// <summary>Sample index of a tick, clamped to the trace.</summary>
    public int IndexOf(int tick) => Math.Clamp(tick - StartTick, 0, Math.Max(Count - 1, 0));
}

public static class EncounterTraceBuilder
{
    public const float LeadSeconds = 0.5f;

    /// <summary>Null when the player or opponent has no recorded state in the encounter.</summary>
    public static EncounterTrace? Build(ParsedDemo demo, Encounter encounter)
    {
        var timeline = demo.Timeline;
        var start = Math.Max(timeline.FirstTick, encounter.StartTick - (int)(LeadSeconds * demo.TickRate));
        var count = encounter.EndTick - start + 1;
        if (count <= 0)
            return null;

        var x = new float[count];
        var y = new float[count];
        var body = new float[count];
        var speed = new float[count];
        var distance = new float[count];
        var reference = System.Numerics.Vector2.Zero;
        var any = false;

        for (var i = 0; i < count; i++)
        {
            var tick = start + i;
            if (!timeline.TryGet(encounter.PlayerSteamId, tick, out var me)
                || !timeline.TryGet(encounter.OpponentSteamId, tick, out var them))
            {
                // Gaps repeat the previous sample so the replay doesn't jump to the origin.
                if (i > 0)
                {
                    x[i] = x[i - 1];
                    y[i] = y[i - 1];
                    body[i] = body[i - 1];
                    speed[i] = speed[i - 1];
                    distance[i] = distance[i - 1];
                }

                continue;
            }

            any = true;
            var signed = BodyModel.SignedError(me, them);
            // SignedError is view minus target: more yaw is further left, more pitch is further down.
            x[i] = -signed.X;
            y[i] = -signed.Y;
            if (tick <= encounter.StartTick || reference == System.Numerics.Vector2.Zero)
                reference = new System.Numerics.Vector2(x[i], y[i]);

            var error = BodyModel.AngularError(me, them);
            var passed = System.Numerics.Vector2.Dot(reference, new System.Numerics.Vector2(x[i], y[i])) < 0;
            body[i] = passed ? -error : error;
            speed[i] = me.HorizontalSpeed;
            distance[i] = BodyModel.Distance(me, them);
        }

        if (!any)
            return null;

        var index = DemoIndex.For(demo);
        var shots = index.ShotsBy(encounter.PlayerSteamId)
            .Where(s => s.Target == encounter.OpponentSteamId && s.Tick >= encounter.StartTick && s.Tick <= encounter.EndTick)
            .Select(s => new TraceShot(s.Tick, s.HitTick is not null, s.HitTick is { } hitTick ? HitGroupOf(index, encounter, hitTick) : null))
            .ToList();

        return new EncounterTrace(start, demo.TickRate, x, y, body, speed, distance, shots);
    }

    private static int? HitGroupOf(DemoIndex index, Encounter encounter, int hitTick) =>
        index.HurtsBy(encounter.PlayerSteamId)
            .FirstOrDefault(h => h.Tick == hitTick && h.VictimSteamId == encounter.OpponentSteamId)?.HitGroup;
}
