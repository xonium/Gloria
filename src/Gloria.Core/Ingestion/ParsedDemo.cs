namespace Gloria.Core.Ingestion;

/// <summary>
/// Normalized output of parsing one demo. This is what gets cached so analysis can be
/// re-run without re-parsing the .dem file.
/// </summary>
public sealed record ParsedDemo(
    string MapName,
    int TickRate,
    int TickCount,
    IReadOnlyList<PlayerInfo> Players,
    IReadOnlyList<PlayerTickState> Ticks,
    IReadOnlyList<DemoEvent> Events);

public sealed record PlayerInfo(ulong SteamId, string Name);

public enum Team
{
    None = 0,
    Spectator = 1,
    Terrorist = 2,
    CounterTerrorist = 3,
}
