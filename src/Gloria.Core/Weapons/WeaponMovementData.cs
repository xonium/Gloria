namespace Gloria.Core.Weapons;

/// <summary>
/// Movement data for one weapon. A player counts as stopped when their speed is at most
/// <see cref="AccurateSpeedFraction"/> of <see cref="MaxSpeed"/>.
/// </summary>
public sealed record WeaponMovementData(string Weapon, float MaxSpeed, float AccurateSpeedFraction)
{
    public float AccurateSpeedThreshold => MaxSpeed * AccurateSpeedFraction;

    public bool IsStopped(float speed) => speed <= AccurateSpeedThreshold;
}

public interface IWeaponData
{
    WeaponMovementData? Find(string weapon);
}
