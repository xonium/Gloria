namespace Gloria.Core.Ingestion;

/// <summary>
/// Normalized output of parsing one demo. This is what gets cached so analysis can be
/// re-run without re-parsing the .dem file.
/// </summary>
/// <param name="Rounds">Live rounds only: warmup and restarted rounds are dropped.</param>
public sealed record ParsedDemo(
    string MapName,
    int TickRate,
    int TickCount,
    IReadOnlyList<PlayerInfo> Players,
    PlayerTimeline Timeline,
    IReadOnlyList<RoundInfo> Rounds,
    IReadOnlyList<DemoEvent> Events)
{
    public float Seconds(int ticks) => ticks / (float)TickRate;
}

public sealed record PlayerInfo(ulong SteamId, string Name);

/// <summary>One live round. Players can move from <see cref="FreezeEndTick"/>.</summary>
public sealed record RoundInfo(int Number, int StartTick, int FreezeEndTick, int EndTick, Team Winner);

public enum Team
{
    None = 0,
    Spectator = 1,
    Terrorist = 2,
    CounterTerrorist = 3,
}
