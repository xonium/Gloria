using System.Numerics;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;

namespace Gloria.Core.Tests.Fixtures;

/// <summary>
/// Builds small <see cref="ParsedDemo"/>s for unit tests: players with scripted poses, spotted ranges,
/// and fire/hurt/death events. Every tick from 0 to the last one is recorded.
/// </summary>
public sealed class SyntheticDemoBuilder
{
    public const int TickRate = 64;

    private readonly List<(ulong Id, Team Team, Vector3 Position)> _players = [];
    private readonly List<(ulong Id, int From, int To, Func<int, Pose> Pose)> _poses = [];
    private readonly List<(ulong Target, ulong Observer, int From, int To)> _spotted = [];
    private readonly List<DemoEvent> _events = [];
    private readonly List<RoundInfo> _rounds = [];

    /// <summary>A player's scripted state. Null fields keep the default (standing still, facing +X).</summary>
    public readonly record struct Pose(Vector3? Position = null, float? Yaw = null, float? Pitch = null, Vector3? Velocity = null, string? Weapon = null, int? Health = null);

    public SyntheticDemoBuilder Player(ulong id, Team team, Vector3 position)
    {
        _players.Add((id, team, position));
        return this;
    }

    public SyntheticDemoBuilder Round(int start, int freezeEnd, int end, Team winner = Team.Terrorist)
    {
        _rounds.Add(new RoundInfo(_rounds.Count + 1, start, freezeEnd, end, winner));
        _events.Add(new RoundStartEvent(start));
        _events.Add(new RoundFreezeEndEvent(freezeEnd));
        _events.Add(new RoundEndEvent(end, winner, 0));
        return this;
    }

    /// <summary>Overrides the player's pose for ticks in [from, to]; later calls win.</summary>
    public SyntheticDemoBuilder Animate(ulong id, int from, int to, Func<int, Pose> pose)
    {
        _poses.Add((id, from, to, pose));
        return this;
    }

    public SyntheticDemoBuilder Spotted(ulong target, ulong by, int from, int to)
    {
        _spotted.Add((target, by, from, to));
        return this;
    }

    public SyntheticDemoBuilder Fire(int tick, ulong shooter, string weapon = "weapon_ak47", int? shotsFired = null)
    {
        _events.Add(new WeaponFireEvent(tick, shooter, weapon, shotsFired));
        return this;
    }

    public SyntheticDemoBuilder Hurt(int tick, ulong victim, ulong? attacker, string weapon = "ak47", int damage = 27, int? healthAfter = null)
    {
        _events.Add(new PlayerHurtEvent(tick, victim, attacker, weapon, damage, 2, healthAfter));
        return this;
    }

    public SyntheticDemoBuilder Death(int tick, ulong victim, ulong? attacker, string weapon = "ak47", bool headshot = false)
    {
        _events.Add(new PlayerDeathEvent(tick, victim, attacker, weapon, headshot));
        return this;
    }

    /// <summary>View angles from the player's eye to the target's upper chest, plus a yaw offset.</summary>
    public (float Yaw, float Pitch) AimAt(ulong player, ulong target, float yawOffset = 0f, float pitchOffset = 0f)
    {
        var from = PositionOf(player) + new Vector3(0, 0, PlayerTickState.StandingEyeHeight);
        var to = PositionOf(target) + new Vector3(0, 0, PlayerTickState.StandingEyeHeight - BodyModel.ChestBelowEye);
        var (yaw, pitch) = AimMath.AnglesTo(from, to);
        return (yaw + yawOffset, pitch + pitchOffset);
    }

    public ParsedDemo Build(int lastTick)
    {
        var deaths = _events.OfType<PlayerDeathEvent>().ToLookup(d => d.VictimSteamId, d => d.Tick);
        var timeline = new PlayerTimelineBuilder();
        foreach (var p in _players)
            timeline.IndexOf(p.Id);

        for (var tick = 0; tick <= lastTick; tick++)
        {
            timeline.BeginTick(tick);
            foreach (var (id, team, position) in _players)
            {
                var pose = _poses.LastOrDefault(p => p.Id == id && tick >= p.From && tick <= p.To) is { Pose: { } f } ? f(tick) : default;
                var dead = deaths[id].Any(d => d < tick);
                var state = new PlayerTickState(
                    tick,
                    id,
                    pose.Position ?? position,
                    pose.Velocity ?? Vector3.Zero,
                    pose.Yaw ?? 0f,
                    pose.Pitch ?? 0f,
                    pose.Weapon ?? "weapon_ak47",
                    !dead,
                    team,
                    Health: dead ? 0 : pose.Health ?? 100);

                ulong spottedBy = 0;
                foreach (var s in _spotted.Where(s => s.Target == id && tick >= s.From && tick <= s.To))
                    spottedBy |= 1UL << timeline.IndexOf(s.Observer);

                timeline.Set(state, spottedBy);
            }
        }

        return new ParsedDemo(
            "de_test",
            TickRate,
            lastTick,
            [.. _players.Select(p => new PlayerInfo(p.Id, $"player{p.Id}"))],
            timeline.Build(),
            _rounds,
            [.. _events.OrderBy(e => e.Tick)]);
    }

    private Vector3 PositionOf(ulong id) => _players.Single(p => p.Id == id).Position;
}
