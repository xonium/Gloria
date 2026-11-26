namespace Gloria.Core.Weapons;

/// <summary>
/// The full-auto guns, the ones a spray means something for, with their magazine size. Semi-auto pistols, snipers
/// (auto-snipers too) and shotguns are left out.
/// </summary>
public static class SprayWeapons
{
    private static readonly Dictionary<string, int> Clips = new(StringComparer.OrdinalIgnoreCase)
    {
        // Rifles
        ["ak47"] = 30,
        ["m4a1"] = 30,
        ["m4a1_silencer"] = 20,
        ["famas"] = 25,
        ["galilar"] = 35,
        ["aug"] = 30,
        ["sg556"] = 30,

        // SMGs
        ["mac10"] = 30,
        ["mp9"] = 30,
        ["mp7"] = 30,
        ["mp5sd"] = 30,
        ["ump45"] = 25,
        ["p90"] = 50,
        ["bizon"] = 64,

        // Machine guns
        ["m249"] = 100,
        ["negev"] = 150,

        // The one full-auto pistol.
        ["cz75a"] = 12,
    };

    /// <summary>The weapons, normalized, in the order above.</summary>
    public static IEnumerable<string> All => Clips.Keys;

    /// <summary>The magazine size of a full-auto gun; false for every other weapon.</summary>
    public static bool TryGetClip(string weapon, out int clip) =>
        Clips.TryGetValue(StaticWeaponData.Normalize(weapon), out clip);
}
