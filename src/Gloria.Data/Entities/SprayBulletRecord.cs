namespace Gloria.Data.Entities;

/// <summary>
/// One player's bullets at one position of their sprays with one full-auto gun, in one demo
/// (<c>Gloria.Core.Sprays.SprayAnalyzer</c>). The sprays with the gun are the rows at bullet 1.
/// </summary>
public sealed class SprayBulletRecord
{
    public int DemoId { get; set; }

    public ulong SteamId { get; set; }

    /// <summary>Normalized weapon name.</summary>
    public string Weapon { get; set; } = "";

    /// <summary>Position in the spray, from 1 at the press of fire.</summary>
    public int Bullet { get; set; }

    /// <summary>Sprays that reached this bullet.</summary>
    public int Fired { get; set; }

    public int Hits { get; set; }

    public int Kills { get; set; }

    public DemoRecord? Demo { get; set; }
}
