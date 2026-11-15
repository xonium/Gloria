namespace Gloria.Core.Weapons;

/// <summary>
/// Movement data for one weapon. A player counts as stopped when their speed is at most
/// <see cref="AccurateSpeedFraction"/> of the weapon's max speed (the scoped one while scoped).
/// </summary>
/// <param name="Weapon">Normalized name, without the <c>weapon_</c> prefix.</param>
/// <param name="ScopedMaxSpeed">Max speed while zoomed in, for weapons that slow down when scoped.</param>
public sealed record WeaponMovementData(
    string Weapon,
    WeaponClass Class,
    float MaxSpeed,
    float AccurateSpeedFraction,
    float? ScopedMaxSpeed = null)
{
    public float AccurateSpeedThreshold => MaxSpeed * AccurateSpeedFraction;

    public float AccurateSpeedThresholdWhen(bool scoped) =>
        (scoped && ScopedMaxSpeed is { } scopedMax ? scopedMax : MaxSpeed) * AccurateSpeedFraction;

    public bool IsStopped(float speed) => speed <= AccurateSpeedThreshold;
}

public interface IWeaponData
{
    WeaponMovementData? Find(string weapon);

    /// <summary>The weapon's class; <see cref="WeaponClass.Other"/> for knives, grenades, the taser, C4 and unknown names.</summary>
    WeaponClass Classify(string weapon);
}
