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

    public DemoRecord? Demo { get; set; }
}
