namespace Gloria.Data.Entities;

/// <summary>A player who appears in a demo.</summary>
public sealed class DemoPlayerRecord
{
    public int DemoId { get; set; }

    public ulong SteamId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>
    /// The player's side in each live round, in round order: 'T', 'C', or '-' when unknown.
    /// Turns round winners into this player's wins and losses.
    /// </summary>
    public string TeamsByRound { get; set; } = "";

    // Scoreboard numbers from every kill, death and hit (PlayerMatchStats). Null for demos imported before they were recorded.
    public int? Kills { get; set; }

    public int? Deaths { get; set; }

    /// <summary>Health taken from enemies, utility included.</summary>
    public int? Damage { get; set; }

    public int? HeadshotKills { get; set; }

    public DemoRecord? Demo { get; set; }
}
