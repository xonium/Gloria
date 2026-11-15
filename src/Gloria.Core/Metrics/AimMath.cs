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

    /// <summary>Signed yaw difference <paramref name="to"/> − <paramref name="from"/>, wrapped to [-180, 180).</summary>
    public static float YawDelta(float from, float to)
    {
        var delta = (to - from) % 360f;
        if (delta >= 180f) delta -= 360f;
        if (delta < -180f) delta += 360f;
        return delta;
    }
}
