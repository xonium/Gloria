using System.Numerics;

namespace Gloria.Core.Ingestion;

/// <summary>
/// One player's recorded state at one tick. Yaw/pitch are the recorded view angles in
/// degrees (where the crosshair was), not the bullet direction, which recoil and aim punch offset.
/// </summary>
/// <param name="EyeHeight">Height of the eye above <see cref="Position"/> (lower while crouched).</param>
/// <param name="AimPunchYaw">
/// Aim punch in degrees at this tick (decays to zero within a second of the last shot). The bullet goes along the view angles plus
/// <see cref="Metrics.Recoil.Scale"/> times this; see <see cref="Metrics.Recoil"/>.
/// </param>
/// <param name="AimPunchPitch">Aim punch pitch in degrees; negative kicks the bullet upwards.</param>
public readonly record struct PlayerTickState(
    int Tick,
    ulong SteamId,
    Vector3 Position,
    Vector3 Velocity,
    float Yaw,
    float Pitch,
    string ActiveWeapon,
    bool IsAlive,
    Team Team,
    float EyeHeight = PlayerTickState.StandingEyeHeight,
    bool IsScoped = false,
    bool IsOnGround = true,
    bool IsDucking = false,
    int Health = 100,
    float AimPunchYaw = 0,
    float AimPunchPitch = 0)
{
    public const float StandingEyeHeight = 64.06f;
    public const float CrouchingEyeHeight = 46.04f;

    public Vector3 EyePosition => Position with { Z = Position.Z + EyeHeight };

    public float HorizontalSpeed => MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Y * Velocity.Y);
}
