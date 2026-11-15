namespace Gloria.Core.Ingestion;

/// <summary>Base type for the game events the analysis needs, stamped with the demo tick they occurred on.</summary>
public abstract record DemoEvent(int Tick);

public sealed record WeaponFireEvent(int Tick, ulong ShooterSteamId, string Weapon) : DemoEvent(Tick);

public sealed record PlayerHurtEvent(
    int Tick,
    ulong VictimSteamId,
    ulong? AttackerSteamId,
    string Weapon,
    int DamageHealth,
    int HitGroup) : DemoEvent(Tick);

public sealed record PlayerDeathEvent(
    int Tick,
    ulong VictimSteamId,
    ulong? AttackerSteamId,
    string Weapon,
    bool Headshot) : DemoEvent(Tick);

public sealed record RoundStartEvent(int Tick) : DemoEvent(Tick);

public sealed record RoundEndEvent(int Tick, Team Winner, int Reason) : DemoEvent(Tick);

public sealed record RoundFreezeEndEvent(int Tick) : DemoEvent(Tick);

/// <summary>The match went live (after warmup or a restart).</summary>
public sealed record MatchStartEvent(int Tick) : DemoEvent(Tick);
