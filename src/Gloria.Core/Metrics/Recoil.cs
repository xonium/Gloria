using System.Numerics;
using Gloria.Core.Ingestion;

namespace Gloria.Core.Metrics;

/// <summary>
/// Where a bullet went: the view angles plus <see cref="Scale"/> times the aim punch. The game's random spread
/// (inaccuracy) is not in the demo, so the real bullet lands somewhere around this direction.
/// </summary>
public static class Recoil
{
    /// <summary>
    /// CS2's weapon_recoil_scale. Fitted on the demos: hits land closest to the body at 1.9 overall and at
    /// 1.75 to 2.15 for every position in a burst, so the game's value is used.
    /// </summary>
    public const float Scale = 2f;

    // The game's punch decay: weapon_recoil_decay2_exp, weapon_recoil_decay2_lin (degrees a second) and weapon_recoil_vel_decay.
    private const float ExponentialDecay = 8f;
    private const float LinearDecay = 18f;
    private const float VelocityDecay = 4.5f;
    private const float StepSeconds = 1f / 128;

    /// <summary>After this long without a new kick the punch is under 0.02°, so it counts as gone.</summary>
    public const float SettledSeconds = 1f;

    /// <summary>
    /// Aim punch <paramref name="seconds"/> after it was <paramref name="punch"/> with velocity <paramref name="velocity"/>
    /// (degrees, degrees a second), stepped the way the game decays it every frame. Negative times return the punch as is.
    /// </summary>
    public static Vector2 Evolve(Vector2 punch, Vector2 velocity, float seconds)
    {
        if (seconds >= SettledSeconds)
            return Vector2.Zero;

        for (var remaining = seconds; remaining > 1e-6f; remaining -= StepSeconds)
        {
            var dt = MathF.Min(StepSeconds, remaining);
            punch *= MathF.Exp(-ExponentialDecay * dt);
            var length = punch.Length();
            punch = length > LinearDecay * dt ? punch * (1 - LinearDecay * dt / length) : Vector2.Zero;
            punch += velocity * (dt * 0.5f);
            velocity *= MathF.Exp(-VelocityDecay * dt);
            punch += velocity * (dt * 0.5f);
        }

        return punch;
    }

    /// <summary>Bullet direction for the view angles and aim punch, with yaw wrapped to [-180, 180).</summary>
    public static (float Yaw, float Pitch) BulletAngles(float yaw, float pitch, float punchYaw, float punchPitch) =>
        (AimMath.YawDelta(0f, yaw + Scale * punchYaw), pitch + Scale * punchPitch);

    /// <summary>The state with its view angles turned to the bullet direction, for the <see cref="BodyModel"/> measures.</summary>
    public static PlayerTickState BulletView(in PlayerTickState state)
    {
        var (yaw, pitch) = BulletAngles(state.Yaw, state.Pitch, state.AimPunchYaw, state.AimPunchPitch);
        return state with { Yaw = yaw, Pitch = pitch };
    }
}
