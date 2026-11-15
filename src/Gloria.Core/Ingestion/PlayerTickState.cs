using System.Numerics;

namespace Gloria.Core.Ingestion;

/// <summary>
/// One player's recorded state at one tick. Yaw/pitch are the recorded view angles in
/// degrees (where the crosshair was), not the bullet direction, which recoil and aim punch offset.
/// </summary>
public readonly record struct PlayerTickState(
    int Tick,
    ulong SteamId,
    Vector3 Position,
    Vector3 Velocity,
    float Yaw,
    float Pitch,
    string ActiveWeapon,
    bool IsAlive,
    Team Team);
