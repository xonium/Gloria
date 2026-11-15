using System.Numerics;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;

namespace Gloria.Core.Tests.Metrics;

public class RecoilTests
{
    private const float Tolerance = 1e-3f;

    [Fact]
    public void BulletAngles_WithoutPunch_AreTheViewAngles()
    {
        var (yaw, pitch) = Recoil.BulletAngles(30f, 5f, 0f, 0f);

        Assert.Equal(30f, yaw, Tolerance);
        Assert.Equal(5f, pitch, Tolerance);
    }

    [Fact]
    public void BulletAngles_NegativePunchPitch_SendsTheBulletAboveTheCrosshair()
    {
        var (_, pitch) = Recoil.BulletAngles(0f, 2f, 0f, -1f);

        // Positive pitch looks down, so a smaller pitch is higher on screen.
        Assert.Equal(2f - Recoil.Scale, pitch, Tolerance);
    }

    [Fact]
    public void BulletAngles_WrapYawAcross180()
    {
        var (yaw, _) = Recoil.BulletAngles(179f, 0f, 1f, 0f);

        Assert.Equal(179f + Recoil.Scale - 360f, yaw, Tolerance);
    }

    [Fact]
    public void BulletDirection_IsScaleTimesThePunchAwayFromTheView()
    {
        var (yaw, pitch) = Recoil.BulletAngles(45f, 0f, 0.3f, -0.4f);

        Assert.Equal(Recoil.Scale * 0.5f, AimMath.AngleBetween(45f, 0f, yaw, pitch), 0.01f);
    }

    [Fact]
    public void Evolve_AtTheBaseTime_IsTheBasePunch()
    {
        var punch = new Vector2(-1.5f, 0.4f);

        Assert.Equal(punch, Recoil.Evolve(punch, new Vector2(-30f, 5f), 0f));
        Assert.Equal(punch, Recoil.Evolve(punch, new Vector2(-30f, 5f), -0.01f));
    }

    [Fact]
    public void Evolve_WithoutVelocity_DecaysToZero()
    {
        var punch = new Vector2(-2f, 0f);

        var soon = Recoil.Evolve(punch, Vector2.Zero, 0.05f);
        var later = Recoil.Evolve(punch, Vector2.Zero, 0.1f);

        // 0.05 s of exp(-8 t) and 18°/s linear decay leave about 0.58° of the 2°.
        Assert.InRange(soon.X, -0.65f, -0.5f);
        Assert.InRange(later.X, soon.X, 0f);
        Assert.Equal(Vector2.Zero, Recoil.Evolve(punch, Vector2.Zero, 0.2f));
    }

    [Fact]
    public void Evolve_Velocity_KeepsKickingBeforeTheDecayWins()
    {
        var punch = new Vector2(-1f, 0f);
        var velocity = new Vector2(-60f, 0f);

        Assert.True(Recoil.Evolve(punch, velocity, 0.05f).X < -1f);
        Assert.Equal(Vector2.Zero, Recoil.Evolve(punch, velocity, Recoil.SettledSeconds));
    }

    [Fact]
    public void BulletView_ChangesOnlyTheViewAngles()
    {
        var state = new PlayerTickState(7, 1, new Vector3(1, 2, 3), new Vector3(4, 5, 6), 10f, 3f, "weapon_ak47", true, Team.Terrorist,
            AimPunchYaw: 0.5f, AimPunchPitch: -1f);

        var bullet = Recoil.BulletView(state);

        Assert.Equal(10f + Recoil.Scale * 0.5f, bullet.Yaw, Tolerance);
        Assert.Equal(3f - Recoil.Scale, bullet.Pitch, Tolerance);
        Assert.Equal(state with { Yaw = bullet.Yaw, Pitch = bullet.Pitch }, bullet);
    }
}
