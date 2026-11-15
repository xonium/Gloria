using System.Numerics;
using Gloria.Core.Metrics;

namespace Gloria.Core.Tests.Metrics;

public class AimMathTests
{
    private const float Tolerance = 1e-3f;

    [Fact]
    public void AngularError_IsZero_WhenLookingStraightAtTarget()
    {
        var error = AimMath.AngularError(0f, 0f, Vector3.Zero, new Vector3(100f, 0f, 0f));

        Assert.Equal(0f, error, Tolerance);
    }

    [Fact]
    public void AngularError_Is90_WhenTargetIsToTheSide()
    {
        var error = AimMath.AngularError(0f, 0f, Vector3.Zero, new Vector3(0f, 100f, 0f));

        Assert.Equal(90f, error, Tolerance);
    }

    [Fact]
    public void AngularError_UsesPositivePitchAsLookingDown()
    {
        var below = new Vector3(100f, 0f, -100f);

        Assert.Equal(0f, AimMath.AngularError(0f, 45f, Vector3.Zero, below), Tolerance);
        Assert.Equal(90f, AimMath.AngularError(0f, -45f, Vector3.Zero, below), Tolerance);
    }

    [Theory]
    [InlineData(100f, 0f, 0f, 0f, 0f)]
    [InlineData(0f, 100f, 0f, 90f, 0f)]
    [InlineData(-100f, 0f, 0f, 180f, 0f)]
    [InlineData(100f, 0f, 100f, 0f, -45f)]
    public void AnglesTo_ReturnsViewAnglesThatPointAtTarget(float x, float y, float z, float expectedYaw, float expectedPitch)
    {
        var (yaw, pitch) = AimMath.AnglesTo(Vector3.Zero, new Vector3(x, y, z));

        Assert.Equal(expectedYaw, yaw, Tolerance);
        Assert.Equal(expectedPitch, pitch, Tolerance);
    }

    [Fact]
    public void AnglesTo_RoundTripsThroughViewDirection()
    {
        var target = new Vector3(37f, -81f, 12f);
        var (yaw, pitch) = AimMath.AnglesTo(Vector3.Zero, target);

        var direction = AimMath.ViewDirection(yaw, pitch);

        Assert.Equal(0f, AimMath.AngularError(yaw, pitch, Vector3.Zero, target), 0.01f);
        Assert.Equal(1f, direction.Length(), Tolerance);
    }

    [Theory]
    [InlineData(10f, 20f, 10f)]
    [InlineData(20f, 10f, -10f)]
    [InlineData(170f, -170f, 20f)]
    [InlineData(-170f, 170f, -20f)]
    [InlineData(0f, 180f, -180f)]
    public void YawDelta_WrapsAcrossPlusMinus180(float from, float to, float expected)
    {
        Assert.Equal(expected, AimMath.YawDelta(from, to), Tolerance);
    }

    [Theory]
    [InlineData(0f)]      // eye height
    [InlineData(1.5f)]    // down toward the hips
    [InlineData(-0.1f)]   // toward the head
    public void AngularErrorToSegment_IsZero_AnywhereOnTheBodyAxis(float pitch)
    {
        var eye = new Vector3(0, 0, 64);

        var error = AimMath.AngularErrorToSegment(0f, pitch, eye, new Vector3(1000, 0, 20), new Vector3(1000, 0, 67));

        Assert.Equal(0f, error, 0.01f);
    }

    [Fact]
    public void AngularErrorToSegment_MeasuresToTheNearestEnd_WhenAimingAbove()
    {
        var eye = new Vector3(0, 0, 64);
        var top = new Vector3(1000, 0, 67);

        var error = AimMath.AngularErrorToSegment(0f, -10f, eye, new Vector3(1000, 0, 20), top);

        Assert.Equal(AimMath.AngularError(0f, -10f, eye, top), error, 0.01f);
    }

    [Fact]
    public void SignedError_IsPositiveYaw_WhenTheViewIsRotatedPastTheTargetCounterClockwise()
    {
        var error = AimMath.SignedError(5f, 0f, Vector3.Zero, new Vector3(1000, 0, 0));

        Assert.Equal(5f, error.X, 0.01f);
        Assert.Equal(0f, error.Y, 0.01f);
    }

    [Fact]
    public void LerpYaw_TakesTheShortWayAround()
    {
        Assert.Equal(-180f, Math.Abs(AimMath.LerpYaw(170f, -170f, 0.5f)) * -1, Tolerance);
        Assert.Equal(5f, AimMath.LerpYaw(0f, 10f, 0.5f), Tolerance);
    }
}
