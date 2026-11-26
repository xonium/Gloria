namespace Gloria.Core.Ingestion;

/// <summary>Base type for the game events the analysis needs, stamped with the demo tick they occurred on.</summary>
public abstract record DemoEvent(int Tick);

/// <param name="ShotsFired">
/// The shooter's consecutive-shot counter (<c>m_iShotsFired</c>) after this shot: it counts up while fire is held and
/// resets when it is released. Null when the parser couldn't read it.
/// </param>
public sealed record WeaponFireEvent(int Tick, ulong ShooterSteamId, string Weapon, int? ShotsFired = null) : DemoEvent(Tick);

/// <param name="DamageHealth">The damage the game reports. It can be off by one and isn't capped at the health left.</param>
/// <param name="HealthAfter">The victim's health after the hit, when the parser read it.</param>
public sealed record PlayerHurtEvent(
    int Tick,
    ulong VictimSteamId,
    ulong? AttackerSteamId,
    string Weapon,
    int DamageHealth,
    int HitGroup,
    int? HealthAfter = null) : DemoEvent(Tick);

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
