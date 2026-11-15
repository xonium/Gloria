using System.Numerics;

namespace Gloria.Core.Metrics;

/// <summary>
/// View-angle geometry in Source engine conventions: yaw in degrees around +Z (0 = +X, 90 = +Y),
/// pitch in degrees with positive values looking down.
/// </summary>
public static class AimMath
{
    /// <summary>Unit direction vector for the given view angles.</summary>
    public static Vector3 ViewDirection(float yawDegrees, float pitchDegrees)
    {
        var yaw = float.DegreesToRadians(yawDegrees);
        var pitch = float.DegreesToRadians(pitchDegrees);
        var cosPitch = MathF.Cos(pitch);
        return new Vector3(cosPitch * MathF.Cos(yaw), cosPitch * MathF.Sin(yaw), -MathF.Sin(pitch));
    }

    /// <summary>View angles that would look from <paramref name="from"/> straight at <paramref name="to"/>.</summary>
    public static (float Yaw, float Pitch) AnglesTo(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var yaw = float.RadiansToDegrees(MathF.Atan2(d.Y, d.X));
        var horizontal = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        var pitch = float.RadiansToDegrees(MathF.Atan2(-d.Z, horizontal));
        return (yaw, pitch);
    }

    /// <summary>Angle in degrees (0..180) between the view direction and the direction to the target.</summary>
    public static float AngularError(float yawDegrees, float pitchDegrees, Vector3 eyePosition, Vector3 targetPosition)
    {
        var view = ViewDirection(yawDegrees, pitchDegrees);
        var toTarget = Vector3.Normalize(targetPosition - eyePosition);
        // atan2 of |cross| and dot stays precise at small angles, where acos(dot) does not.
        var sin = Vector3.Cross(view, toTarget).Length();
        var cos = Vector3.Dot(view, toTarget);
        return float.RadiansToDegrees(MathF.Atan2(sin, cos));
    }

    /// <summary>Angle in degrees between two sets of view angles.</summary>
    public static float AngleBetween(float yawA, float pitchA, float yawB, float pitchB)
    {
        var a = ViewDirection(yawA, pitchA);
        var b = ViewDirection(yawB, pitchB);
        return float.RadiansToDegrees(MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b)));
    }

    /// <summary>
    /// Angle in degrees from the view ray to the nearest point of the segment
    /// <paramref name="bottom"/>–<paramref name="top"/>, e.g. a player's body axis. Aiming anywhere
    /// from the hips to the head counts as zero error, so head and body aim are not penalized.
    /// </summary>
    public static float AngularErrorToSegment(float yawDegrees, float pitchDegrees, Vector3 eyePosition, Vector3 bottom, Vector3 top)
    {
        var view = ViewDirection(yawDegrees, pitchDegrees);
        var axis = top - bottom;
        var toBottom = bottom - eyePosition;

        // Closest points between the view line eye + t*view and the segment bottom + s*axis.
        var b = Vector3.Dot(view, axis);
        var c = Vector3.Dot(axis, axis);
        var d = Vector3.Dot(view, toBottom);
        var e = Vector3.Dot(axis, toBottom);
        var denominator = c - b * b; // |view| = 1
        var s = denominator > 1e-6f ? (b * d - e) / denominator : 0f;
        s = Math.Clamp(s, 0f, 1f);

        return AngularError(yawDegrees, pitchDegrees, eyePosition, bottom + s * axis);
    }

    /// <summary>
    /// Signed 2D aim error in degrees: (yaw, pitch) of the view minus the angles that look at the target,
    /// with yaw scaled by cos(pitch) so both axes measure arc on the screen.
    /// </summary>
    public static Vector2 SignedError(float yawDegrees, float pitchDegrees, Vector3 eyePosition, Vector3 targetPosition)
    {
        var (yaw, pitch) = AnglesTo(eyePosition, targetPosition);
        var cosPitch = MathF.Cos(float.DegreesToRadians(pitch));
        return new Vector2(YawDelta(yaw, yawDegrees) * cosPitch, pitchDegrees - pitch);
    }

    /// <summary>Interpolates yaw along the shorter way around.</summary>
    public static float LerpYaw(float from, float to, float amount)
    {
        var yaw = from + YawDelta(from, to) * amount;
        return yaw >= 180f ? yaw - 360f : yaw < -180f ? yaw + 360f : yaw;
    }

    /// <summary>Signed yaw difference <paramref name="to"/> − <paramref name="from"/>, wrapped to [-180, 180).</summary>
    public static float YawDelta(float from, float to)
    {
        var delta = (to - from) % 360f;
        if (delta >= 180f) delta -= 360f;
        if (delta < -180f) delta += 360f;
        return delta;
    }
}
