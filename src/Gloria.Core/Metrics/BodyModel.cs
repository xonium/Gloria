using System.Numerics;
using Gloria.Core.Ingestion;

namespace Gloria.Core.Metrics;

/// <summary>Approximate aim points on a player model, derived from their origin and eye height.</summary>
public static class BodyModel
{
    /// <summary>Lowest point that counts as on target (around the hips).</summary>
    public const float HipHeight = 20f;

    /// <summary>The head extends this far above the eye.</summary>
    public const float AboveEye = 3f;

    /// <summary>The upper chest sits this far below the eye; used as the single reference point.</summary>
    public const float ChestBelowEye = 12f;

    /// <summary>Half the width of a player model, in units.</summary>
    public const float HalfWidth = 16f;

    /// <summary>
    /// Angle a body half-width spans at the given distance. Errors are measured to the body axis, so an
    /// error below this means the crosshair was on the body; the tolerance shrinks with range.
    /// </summary>
    public static float HalfWidthDegrees(float distance) => float.RadiansToDegrees(MathF.Atan(HalfWidth / MathF.Max(distance, 1f)));

    public static Vector3 Bottom(in PlayerTickState target) => target.Position with { Z = target.Position.Z + HipHeight };

    public static Vector3 Top(in PlayerTickState target) => target.Position with { Z = target.Position.Z + target.EyeHeight + AboveEye };

    public static Vector3 Chest(in PlayerTickState target) => target.Position with { Z = target.Position.Z + target.EyeHeight - ChestBelowEye };

    /// <summary>Angle from the observer's crosshair to the nearest point of the target's body.</summary>
    public static float AngularError(in PlayerTickState observer, in PlayerTickState target) =>
        AimMath.AngularErrorToSegment(observer.Yaw, observer.Pitch, observer.EyePosition, Bottom(target), Top(target));

    /// <summary>Signed error from the observer's crosshair to the target's upper chest.</summary>
    public static Vector2 SignedError(in PlayerTickState observer, in PlayerTickState target) =>
        AimMath.SignedError(observer.Yaw, observer.Pitch, observer.EyePosition, Chest(target));

    public static float Distance(in PlayerTickState observer, in PlayerTickState target) =>
        Vector3.Distance(observer.EyePosition, Chest(target));

    /// <summary>State between two recorded ticks, for shots that happened between them (sub-tick input).</summary>
    public static PlayerTickState Lerp(in PlayerTickState from, in PlayerTickState to, float amount) => to with
    {
        Position = Vector3.Lerp(from.Position, to.Position, amount),
        Velocity = Vector3.Lerp(from.Velocity, to.Velocity, amount),
        Yaw = AimMath.LerpYaw(from.Yaw, to.Yaw, amount),
        Pitch = float.Lerp(from.Pitch, to.Pitch, amount),
        EyeHeight = float.Lerp(from.EyeHeight, to.EyeHeight, amount),
    };
}
