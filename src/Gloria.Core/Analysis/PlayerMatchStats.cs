using Gloria.Core.Ingestion;

namespace Gloria.Core.Analysis;

/// <summary>A player's scoreboard numbers for one demo, counted from every kill, death and hit in live rounds.</summary>
/// <param name="Kills">Enemies killed, minus teammates killed, as the scoreboard counts them.</param>
/// <param name="Deaths">Every death, suicides and world damage included.</param>
/// <param name="Damage">Health taken from enemies, utility included, capped at the health they had left.</param>
/// <param name="HeadshotKills">Enemy kills with a headshot.</param>
public sealed record PlayerMatchStats(int Kills, int Deaths, int Damage, int HeadshotKills)
{
    /// <summary>How long after a round ends its kills and damage still count (the game waits about 7 s).</summary>
    private const int AfterRoundSeconds = 10;

    public static IReadOnlyDictionary<ulong, PlayerMatchStats> Compute(ParsedDemo demo)
    {
        var tallies = demo.Players.DistinctBy(p => p.SteamId).ToDictionary(p => p.SteamId, _ => new Tally());
        if (demo.Rounds.Count == 0)
            return tallies.ToDictionary(t => t.Key, t => t.Value.ToStats());

        var health = new Dictionary<ulong, int>();
        var round = -1;
        foreach (var e in demo.Events.OrderBy(e => e.Tick))
        {
            while (round + 1 < demo.Rounds.Count && e.Tick >= demo.Rounds[round + 1].StartTick)
            {
                round++;
                health.Clear();
            }

            if (round < 0 || e.Tick > demo.Rounds[round].EndTick + AfterRoundSeconds * demo.TickRate)
                continue;

            switch (e)
            {
                case PlayerHurtEvent hurt:
                {
                    // Every hit moves the victim's health, falls and self-damage too, so the next hit is capped right.
                    var before = health.TryGetValue(hurt.VictimSteamId, out var h) ? h : HealthBefore(demo, hurt.VictimSteamId, hurt.Tick);
                    var after = Math.Clamp(hurt.HealthAfter ?? before - hurt.DamageHealth, 0, before);
                    health[hurt.VictimSteamId] = after;
                    if (hurt.AttackerSteamId is { } attacker && attacker != hurt.VictimSteamId
                        && AreEnemies(demo, attacker, hurt.VictimSteamId, hurt.Tick, round))
                        Get(tallies, attacker).Damage += before - after;
                    break;
                }

                case PlayerDeathEvent death:
                {
                    Get(tallies, death.VictimSteamId).Deaths++;
                    if (death.AttackerSteamId is not { } attacker || attacker == death.VictimSteamId)
                        break;

                    var killer = Get(tallies, attacker);
                    if (AreEnemies(demo, attacker, death.VictimSteamId, death.Tick, round))
                    {
                        killer.Kills++;
                        if (death.Headshot)
                            killer.HeadshotKills++;
                    }
                    else if (TeamOf(demo, attacker, death.Tick, round) != Team.None)
                    {
                        killer.Kills--;
                    }

                    break;
                }
            }
        }

        return tallies.ToDictionary(t => t.Key, t => t.Value.ToStats());
    }

    private static Tally Get(Dictionary<ulong, Tally> tallies, ulong steamId)
    {
        if (!tallies.TryGetValue(steamId, out var tally))
            tallies[steamId] = tally = new Tally();
        return tally;
    }

    /// <summary>The victim's health just before the tick, or full health when it wasn't recorded.</summary>
    private static int HealthBefore(ParsedDemo demo, ulong victim, int tick) =>
        demo.Timeline.TryGet(victim, tick - 1, out var state) && state.Health > 0 ? state.Health : 100;

    private static bool AreEnemies(ParsedDemo demo, ulong a, ulong b, int tick, int round)
    {
        var teamA = TeamOf(demo, a, tick, round);
        var teamB = TeamOf(demo, b, tick, round);
        return teamA != Team.None && teamB != Team.None && teamA != teamB;
    }

    /// <summary>The player's side at the tick, or else in the round; <see cref="Team.None"/> when unknown.</summary>
    private static Team TeamOf(ParsedDemo demo, ulong steamId, int tick, int round)
    {
        var r = demo.Rounds[round];
        foreach (var t in new[] { tick, tick - 1, r.FreezeEndTick, r.FreezeEndTick + demo.TickRate, r.EndTick })
        {
            if (demo.Timeline.TryGet(steamId, t, out var state) && state.Team is Team.Terrorist or Team.CounterTerrorist)
                return state.Team;
        }

        return Team.None;
    }

    private sealed class Tally
    {
        public int Kills;
        public int Deaths;
        public int Damage;
        public int HeadshotKills;

        public PlayerMatchStats ToStats() => new(Kills, Deaths, Damage, HeadshotKills);
    }
}
