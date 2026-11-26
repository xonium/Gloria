using System.Numerics;
using DemoFile;
using DemoFile.Game.Cs;
using Gloria.Core.Metrics;

namespace Gloria.Core.Ingestion;

/// <summary><see cref="IDemoParser"/> built on the managed DemoFile.Net parser.</summary>
public sealed class DemoFileDemoParser : IDemoParser
{
    // Entity flag set while the player stands on the ground (FL_ONGROUND).
    private const uint OnGroundFlag = 1;

    // 5: the first live round is no longer dropped when the match start is announced during it.
    // 6: hurt events carry the victim's remaining health, for damage per round.
    // 7: fire events carry the shooter's consecutive-shot counter, for sprays.
    public int Version => 7;

    public async Task<ParsedDemo> ParseAsync(Stream demo, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var parser = new CsDemoParser();
        var mapName = "";
        var players = new Dictionary<ulong, PlayerInfo>();
        var events = new List<DemoEvent>();
        var timeline = new PlayerTimelineBuilder();
        var rounds = new RoundTracker();

        // Fire events whose shot counter is read once the tick's entity updates have arrived.
        var pendingFires = new List<(int EventIndex, CCSPlayerPawn Pawn)>();

        int Tick() => parser.CurrentDemoTick.Value;

        // parser.GameRules throws until the game rules proxy entity has been created.
        var hasGameRules = false;
        parser.EntityEvents.CCSGameRulesProxy.Create += _ => hasGameRules = true;
        bool IsWarmup() => !hasGameRules || parser.GameRules.WarmupPeriod;

        ulong? SteamIdOf(CCSPlayerController? player)
        {
            if (player is not { SteamID: not 0 })
            {
                return null;
            }

            players.TryAdd(player.SteamID, new PlayerInfo(player.SteamID, player.PlayerName));
            return player.SteamID;
        }

        parser.DemoEvents.DemoFileHeader += header => mapName = header.MapName;
        parser.Source1GameEvents.RoundAnnounceMatchStart += _ =>
        {
            events.Add(new MatchStartEvent(Tick()));
            rounds.MatchStart(Tick());
        };
        parser.Source1GameEvents.RoundStart += _ =>
        {
            events.Add(new RoundStartEvent(Tick()));
            rounds.Start(Tick(), IsWarmup());
        };
        parser.Source1GameEvents.RoundFreezeEnd += _ =>
        {
            events.Add(new RoundFreezeEndEvent(Tick()));
            rounds.FreezeEnd(Tick());
        };
        parser.Source1GameEvents.RoundEnd += e =>
        {
            events.Add(new RoundEndEvent(Tick(), (Team)e.Winner, e.Reason));
            rounds.End(Tick(), (Team)e.Winner);
        };
        parser.Source1GameEvents.WeaponFire += e =>
        {
            if (SteamIdOf(e.Player) is { } shooter)
            {
                if (e.Player?.PlayerPawn is { } pawn)
                {
                    pendingFires.Add((events.Count, pawn));
                }

                events.Add(new WeaponFireEvent(Tick(), shooter, e.Weapon));
            }
        };
        parser.Source1GameEvents.PlayerHurt += e =>
        {
            if (SteamIdOf(e.Player) is { } victim)
            {
                events.Add(new PlayerHurtEvent(Tick(), victim, SteamIdOf(e.Attacker), e.Weapon, e.DmgHealth, e.Hitgroup, e.Health));
            }
        };
        parser.Source1GameEvents.PlayerDeath += e =>
        {
            if (SteamIdOf(e.Player) is { } victim)
            {
                events.Add(new PlayerDeathEvent(Tick(), victim, SteamIdOf(e.Attacker), e.Weapon, e.Headshot));
            }
        };

        // Sample after every command; a tick can span several commands, so later ones overwrite the row.
        parser.OnCommandFinishPersistent = () =>
        {
            ResolveShotCounters(events, pendingFires);
            if (IsWarmup())
            {
                return;
            }

            SampleTick(parser, timeline, Tick());
        };

        var reader = DemoFileReader.Create(parser, demo);
        if (progress is not null)
        {
            reader.OnProgress = p => progress.Report(p.ProgressRatio);
        }

        await reader.ReadAllAsync(cancellationToken);
        ResolveShotCounters(events, pendingFires);

        foreach (var player in parser.PlayersIncludingDisconnected)
        {
            SteamIdOf(player);
        }

        if (!timeline.HasNetworkedVelocity())
        {
            timeline.DeriveVelocityFromPositions(CsDemoParser.TickRate);
        }

        progress?.Report(1.0);
        return new ParsedDemo(
            mapName,
            CsDemoParser.TickRate,
            parser.TickCount.Value,
            [.. players.Values],
            timeline.Build(),
            rounds.Rounds,
            events);
    }

    /// <summary>
    /// Fills in the shooter's <c>m_iShotsFired</c> on fire events. The game event can arrive before the pawn's update
    /// for the same tick, so the counter is read after the command, when it includes the shot.
    /// </summary>
    private static void ResolveShotCounters(List<DemoEvent> events, List<(int EventIndex, CCSPlayerPawn Pawn)> pending)
    {
        foreach (var (eventIndex, pawn) in pending)
        {
            if (events[eventIndex] is WeaponFireEvent fire)
            {
                events[eventIndex] = fire with { ShotsFired = pawn.IsActive ? pawn.ShotsFired : null };
            }
        }

        pending.Clear();
    }

    private static void SampleTick(CsDemoParser parser, PlayerTimelineBuilder timeline, int tick)
    {
        // Map entity slots to local player indexes first, so spotted masks can be resolved.
        Span<int> localBySlot = stackalloc int[128];
        localBySlot.Fill(-1);
        var tracked = new List<(CCSPlayerController Controller, CCSPlayerPawn Pawn, int Index)>(12);
        foreach (var controller in parser.PlayersIncludingDisconnected)
        {
            if (controller.SteamID == 0 || controller.PlayerPawn is not { IsActive: true } pawn)
            {
                continue;
            }

            var index = timeline.IndexOf(controller.SteamID);
            var slot = (int)controller.EntityIndex.Value - 1;
            if (slot >= 0 && slot < localBySlot.Length)
            {
                localBySlot[slot] = index;
            }

            tracked.Add((controller, pawn, index));
        }

        if (tracked.Count == 0)
        {
            return;
        }

        timeline.BeginTick(tick);
        var seconds = parser.CurrentGameTick.Value / (float)CsDemoParser.TickRate;
        foreach (var (controller, pawn, index) in tracked)
        {
            timeline.Set(index, ReadState(controller, pawn, tick, seconds), SpottedBy(pawn, localBySlot));
        }
    }

    /// <param name="seconds">Game time of the sample, in seconds.</param>
    private static PlayerTickState ReadState(CCSPlayerController controller, CCSPlayerPawn pawn, int tick, float seconds)
    {
        var origin = pawn.Origin;
        var velocity = pawn.Velocity;
        var angles = pawn.EyeAngles;
        var duck = pawn.MovementServices?.DuckAmount ?? 0f;
        var viewOffset = pawn.ViewOffset.Z;
        var punch = AimPunchAt(pawn, seconds);
        var eyeHeight = viewOffset > 0
            ? viewOffset
            : PlayerTickState.StandingEyeHeight - duck * (PlayerTickState.StandingEyeHeight - PlayerTickState.CrouchingEyeHeight);

        return new PlayerTickState(
            tick,
            controller.SteamID,
            new Vector3(origin.X, origin.Y, origin.Z),
            new Vector3(velocity.X, velocity.Y, velocity.Z),
            angles.Yaw,
            angles.Pitch,
            pawn.ActiveWeapon?.AttributeManager.Item.Name ?? "",
            pawn.IsAlive,
            (Team)controller.CSTeamNum,
            eyeHeight,
            pawn.IsScoped,
            (pawn.Flags & OnGroundFlag) != 0,
            duck > 0.5f,
            pawn.Health,
            punch.Y,
            punch.X);
    }

    /// <summary>
    /// Aim punch (pitch, yaw) at the given game time. The pawn networks the punch and its velocity only when a shot
    /// changes them (the "predictable base", stamped in half ticks); the game evolves them from there, so do the same.
    /// </summary>
    private static Vector2 AimPunchAt(CCSPlayerPawn pawn, float seconds)
    {
        if (pawn.AimPunchServices is not { } punch)
        {
            return Vector2.Zero;
        }

        var baseSeconds = (punch.PredictableBaseTick.Value + punch.PredictableBaseTickInterpAmount) / (2f * CsDemoParser.TickRate);
        return Recoil.Evolve(
            new Vector2(FloatField(punch.PredictableBaseAngle.Pitch), FloatField(punch.PredictableBaseAngle.Yaw)),
            new Vector2(FloatField(punch.PredictableBaseAngleVel.Pitch), FloatField(punch.PredictableBaseAngleVel.Yaw)),
            seconds - baseSeconds);
    }

    /// <summary>
    /// DemoFile.Net 0.45 decodes the aim punch angles as 32-bit quantized angles (raw / 2^32 * 360), but they hold plain
    /// floats. This recovers the float from its bits, to about 15 bits of mantissa. RecoilDemoTests fails if a newer
    /// parser version decodes them properly and this becomes wrong.
    /// </summary>
    private static float FloatField(float decoded) =>
        BitConverter.UInt32BitsToSingle((uint)((long)Math.Round(decoded / 360.0 * 4294967296.0) & 0xFFFFFFFF));

    /// <summary>
    /// The target pawn's spotted mask has bit <c>slot</c> set for each observer that spots it,
    /// where slot is the observer controller's entity index minus one.
    /// </summary>
    private static ulong SpottedBy(CCSPlayerPawn pawn, ReadOnlySpan<int> localBySlot)
    {
        var mask = pawn.EntitySpottedState.SpottedByMask;
        ulong result = 0;
        for (var word = 0; word < mask.Length; word++)
        {
            var bits = mask[word];
            while (bits != 0)
            {
                var bit = BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                var slot = word * 32 + bit;
                if (slot < localBySlot.Length && localBySlot[slot] is >= 0 and < 64 and var local)
                {
                    result |= 1UL << local;
                }
            }
        }

        return result;
    }
}
