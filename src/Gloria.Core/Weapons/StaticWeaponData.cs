namespace Gloria.Core.Weapons;

/// <summary>
/// Seed table of weapon movement data.
/// TODO: source the full table from the game's weapon data instead of hand-entered values.
/// </summary>
public sealed class StaticWeaponData : IWeaponData
{
    // Firing is accurate up to roughly a third of the weapon's max speed.
    private const float DefaultAccurateFraction = 0.34f;

    private static readonly Dictionary<string, WeaponMovementData> Weapons =
        new WeaponMovementData[]
        {
            new("weapon_ak47", 215f, DefaultAccurateFraction),
            new("weapon_m4a1", 225f, DefaultAccurateFraction),
            new("weapon_m4a1_silencer", 225f, DefaultAccurateFraction),
            new("weapon_awp", 200f, DefaultAccurateFraction),
            new("weapon_deagle", 230f, DefaultAccurateFraction),
            new("weapon_glock", 240f, DefaultAccurateFraction),
            new("weapon_usp_silencer", 240f, DefaultAccurateFraction),
        }.ToDictionary(w => w.Weapon, StringComparer.OrdinalIgnoreCase);

    public WeaponMovementData? Find(string weapon)
    {
        if (Weapons.TryGetValue(weapon, out var data))
        {
            return data;
        }

        // Game events report weapons without the "weapon_" prefix.
        return Weapons.GetValueOrDefault("weapon_" + weapon);
    }
}
