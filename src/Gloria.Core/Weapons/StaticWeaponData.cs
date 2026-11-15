namespace Gloria.Core.Weapons;

/// <summary>
/// Movement data for every gun, entered by hand from the game's weapon data.
/// TODO: verify the speeds against the game's items_game / weapon scripts.
/// </summary>
public sealed class StaticWeaponData : IWeaponData
{
    // Firing is accurate up to roughly a third of the weapon's max speed.
    private const float DefaultAccurateFraction = 0.34f;

    private static readonly Dictionary<string, WeaponMovementData> Weapons = Table()
        .ToDictionary(w => w.Weapon, StringComparer.OrdinalIgnoreCase);

    public static StaticWeaponData Instance { get; } = new();

    public WeaponMovementData? Find(string weapon) => Weapons.GetValueOrDefault(Normalize(weapon));

    public WeaponClass Classify(string weapon) => Find(weapon)?.Class ?? WeaponClass.Other;

    /// <summary>Strips the <c>weapon_</c> prefix that entity item names carry and game events omit.</summary>
    public static string Normalize(string weapon)
    {
        var name = weapon.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? weapon["weapon_".Length..] : weapon;
        // A silencer taken off doesn't make it another gun.
        return name.EndsWith("_off", StringComparison.OrdinalIgnoreCase) ? name[..^"_off".Length] : name;
    }

    /// <summary>
    /// The name <c>player_hurt</c> uses for a gun. Hurt events carry the base item, so an M4A1-S hit reads
    /// <c>m4a1</c> and a USP-S hit <c>hkp2000</c>, while <c>weapon_fire</c> and <c>player_death</c> name the variant.
    /// </summary>
    public static string HurtName(string weapon) => Normalize(weapon).ToLowerInvariant() switch
    {
        "m4a1_silencer" => "m4a1",
        "usp_silencer" => "hkp2000",
        "revolver" => "deagle",
        "cz75a" => "p250",
        "mp5sd" => "mp7",
        var name => name,
    };

    /// <summary>Whether a fire event and a hurt event name the same gun.</summary>
    public static bool SameGun(string fireWeapon, string hurtWeapon) => HurtName(fireWeapon) == HurtName(hurtWeapon);

    private static IEnumerable<WeaponMovementData> Table()
    {
        static WeaponMovementData W(string name, WeaponClass c, float max, float? scoped = null) =>
            new(name, c, max, DefaultAccurateFraction, scoped);

        // Pistols
        yield return W("glock", WeaponClass.Pistol, 240);
        yield return W("hkp2000", WeaponClass.Pistol, 240);
        yield return W("usp_silencer", WeaponClass.Pistol, 240);
        yield return W("p250", WeaponClass.Pistol, 240);
        yield return W("elite", WeaponClass.Pistol, 240);
        yield return W("fiveseven", WeaponClass.Pistol, 240);
        yield return W("tec9", WeaponClass.Pistol, 240);
        yield return W("cz75a", WeaponClass.Pistol, 240);
        yield return W("deagle", WeaponClass.Pistol, 230);
        yield return W("revolver", WeaponClass.Pistol, 220);

        // SMGs
        yield return W("mac10", WeaponClass.Smg, 240);
        yield return W("mp9", WeaponClass.Smg, 240);
        yield return W("bizon", WeaponClass.Smg, 240);
        yield return W("mp5sd", WeaponClass.Smg, 235);
        yield return W("ump45", WeaponClass.Smg, 230);
        yield return W("p90", WeaponClass.Smg, 230);
        yield return W("mp7", WeaponClass.Smg, 220);

        // Rifles
        yield return W("ak47", WeaponClass.Rifle, 215);
        yield return W("galilar", WeaponClass.Rifle, 215);
        yield return W("m4a1", WeaponClass.Rifle, 225);
        yield return W("m4a1_silencer", WeaponClass.Rifle, 225);
        yield return W("famas", WeaponClass.Rifle, 220);
        yield return W("aug", WeaponClass.Rifle, 220, scoped: 150);
        yield return W("sg556", WeaponClass.Rifle, 210, scoped: 150);

        // Snipers
        yield return W("awp", WeaponClass.Sniper, 200, scoped: 100);
        yield return W("ssg08", WeaponClass.Sniper, 230);
        yield return W("g3sg1", WeaponClass.Sniper, 215, scoped: 120);
        yield return W("scar20", WeaponClass.Sniper, 215, scoped: 120);

        // Shotguns
        yield return W("nova", WeaponClass.Shotgun, 220);
        yield return W("xm1014", WeaponClass.Shotgun, 215);
        yield return W("mag7", WeaponClass.Shotgun, 225);
        yield return W("sawedoff", WeaponClass.Shotgun, 210);

        // Machine guns
        yield return W("m249", WeaponClass.MachineGun, 195);
        yield return W("negev", WeaponClass.MachineGun, 150);
    }
}
