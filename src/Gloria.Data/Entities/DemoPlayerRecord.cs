namespace Gloria.Data.Entities;

/// <summary>A player who appears in a demo.</summary>
public sealed class DemoPlayerRecord
{
    public int DemoId { get; set; }

    public ulong SteamId { get; set; }

    public string Name { get; set; } = "";

    public DemoRecord? Demo { get; set; }
}
