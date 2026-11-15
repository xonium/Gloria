using Gloria.Core.Ingestion;
using Gloria.Core.Visibility;

namespace Gloria.Core.Encounters;

public sealed record SegmenterOptions
{
    /// <summary>Contact gaps shorter than this do not split an encounter; longer ones end it as a disengage.</summary>
    public float DisengageSeconds { get; init; } = 2f;

    /// <summary>Encounters without a kill or death that last longer than this end as a timeout.</summary>
    public float MaxEncounterSeconds { get; init; } = 10f;

    /// <summary>
    /// Drop encounters where the player never saw the opponent (spam through smokes and walls). The spotted
    /// state updates only every ~33 ticks, so visibility up to <see cref="VisibilityLagSeconds"/> after the
    /// encounter still counts.
    /// </summary>
    public bool RequireVisibility { get; init; } = true;

    public float VisibilityLagSeconds { get; init; } = 0.55f;
}

/// <summary>Encounters plus what was dropped, so the report can show the bias that dropping introduces.</summary>
/// <param name="DroppedWithoutShots">Contact intervals where the player never fired at the opponent.</param>
/// <param name="DroppedDeathsWithoutShots">Of those, how many ended with the player dying.</param>
/// <param name="DroppedUnseen">Encounters with shots where the player never saw the opponent.</param>
public sealed record SegmentationResult(IReadOnlyList<Encounter> Encounters, int DroppedWithoutShots, int DroppedDeathsWithoutShots, int DroppedUnseen);

/// <summary>
/// Splits each live round into encounters between the player and each opponent. Contact is the
/// opponent being visible to the player, damage between the two, or a shot by the player aimed at
/// the opponent. Contact closer together than <see cref="SegmenterOptions.DisengageSeconds"/> is one
/// encounter. Only encounters where the player saw and fired at the opponent are kept.
/// </summary>
public sealed class EncounterSegmenter(IVisibilitySource? visibility = null, SegmenterOptions? options = null) : IEncounterSegmenter
{
    private readonly SegmenterOptions _options = options ?? new SegmenterOptions();

    public int Version => 2;

    public IReadOnlyList<Encounter> Segment(ParsedDemo demo, ulong playerSteamId) =>
        SegmentWithStats(demo, playerSteamId).Encounters;

    /// <summary>Encounters of every tracked player.</summary>
    public SegmentationResult SegmentAll(ParsedDemo demo)
    {
        var encounters = new List<Encounter>();
        int dropped = 0, droppedDeaths = 0, unseen = 0;
        foreach (var player in demo.Timeline.SteamIds)
        {
            var result = SegmentWithStats(demo, player);
            encounters.AddRange(result.Encounters);
            dropped += result.DroppedWithoutShots;
            droppedDeaths += result.DroppedDeathsWithoutShots;
            unseen += result.DroppedUnseen;
        }

        return new SegmentationResult(encounters, dropped, droppedDeaths, unseen);
    }

    public SegmentationResult SegmentWithStats(ParsedDemo demo, ulong playerSteamId)
    {
        var index = DemoIndex.For(demo);
        var source = visibility ?? VisibilitySources.For(demo);
        var gap = (int)MathF.Round(_options.DisengageSeconds * demo.TickRate);
        var maxTicks = (int)MathF.Round(_options.MaxEncounterSeconds * demo.TickRate);
        var lag = (int)MathF.Round(_options.VisibilityLagSeconds * demo.TickRate);
        var shots = index.ShotsBy(playerSteamId);

        var encounters = new List<Encounter>();
        int dropped = 0, droppedDeaths = 0, unseen = 0;

        foreach (var round in demo.Rounds)
        {
            var from = round.FreezeEndTick;
            var to = round.EndTick;
            foreach (var opponent in OpponentsIn(demo, playerSteamId, round))
            {
                var contacts = ContactTicks(demo, index, source, shots, playerSteamId, opponent, from, to);
                foreach (var (start, last) in Merge(contacts, gap))
                {
                    var (end, outcome) = Resolve(index, playerSteamId, opponent, start, last, gap, maxTicks, to);
                    if (!shots.Any(s => s.Target == opponent && s.Tick >= start && s.Tick <= end))
                    {
                        dropped++;
                        if (outcome == EncounterOutcome.Death)
                            droppedDeaths++;
                        continue;
                    }

                    if (_options.RequireVisibility && !Enumerable.Range(start, end + lag - start + 1).Any(t => source.CanSee(demo, t, playerSteamId, opponent)))
                    {
                        unseen++;
                        continue;
                    }

                    encounters.Add(new Encounter(round.Number, playerSteamId, opponent, start, end, outcome));
                }
            }
        }

        encounters.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
        return new SegmentationResult(encounters, dropped, droppedDeaths, unseen);
    }

    private static IEnumerable<ulong> OpponentsIn(ParsedDemo demo, ulong player, RoundInfo round)
    {
        if (!demo.Timeline.TryGet(player, round.FreezeEndTick, out var me))
            return [];

        return demo.Timeline.SteamIds.Where(id =>
            demo.Timeline.TryGet(id, round.FreezeEndTick, out var other) && ShotAttribution.AreOpponents(me.Team, other.Team));
    }

    private static List<int> ContactTicks(
        ParsedDemo demo,
        DemoIndex index,
        IVisibilitySource source,
        IReadOnlyList<AttributedShot> shots,
        ulong player,
        ulong opponent,
        int from,
        int to)
    {
        var ticks = new List<int>();
        for (var tick = from; tick <= to; tick++)
        {
            if (demo.Timeline.TryGet(player, tick, out var me) && me.IsAlive
                && demo.Timeline.TryGet(opponent, tick, out var them) && them.IsAlive
                && source.CanSee(demo, tick, player, opponent))
            {
                ticks.Add(tick);
            }
        }

        ticks.AddRange(index.HurtsBy(player).Where(h => h.VictimSteamId == opponent && h.Tick >= from && h.Tick <= to).Select(h => h.Tick));
        ticks.AddRange(index.HurtsBy(opponent).Where(h => h.VictimSteamId == player && h.Tick >= from && h.Tick <= to).Select(h => h.Tick));
        ticks.AddRange(shots.Where(s => s.Target == opponent && s.Tick >= from && s.Tick <= to).Select(s => s.Tick));
        ticks.Sort();
        return ticks;
    }

    private static IEnumerable<(int Start, int Last)> Merge(List<int> ticks, int gap)
    {
        if (ticks.Count == 0)
            yield break;

        var start = ticks[0];
        var last = ticks[0];
        foreach (var tick in ticks.Skip(1))
        {
            if (tick - last > gap)
            {
                yield return (start, last);
                start = tick;
            }

            last = tick;
        }

        yield return (start, last);
    }

    private static (int End, EncounterOutcome Outcome) Resolve(
        DemoIndex index, ulong player, ulong opponent, int start, int last, int gap, int maxTicks, int roundEnd)
    {
        if (index.DeathOf(opponent, start, last) is { } kill && kill.AttackerSteamId == player)
            return (kill.Tick, EncounterOutcome.Kill);
        if (index.DeathOf(player, start, last) is { } death && death.AttackerSteamId == opponent)
            return (death.Tick, EncounterOutcome.Death);

        // Either one died to someone else, or contact stopped.
        var otherDeath = index.DeathOf(opponent, start, last + gap) ?? index.DeathOf(player, start, last + gap);
        if (otherDeath is not null)
            return (Math.Min(last, otherDeath.Tick), EncounterOutcome.Disengage);

        if (last - start > maxTicks || last + gap >= roundEnd)
            return (last, EncounterOutcome.Timeout);

        return (last, EncounterOutcome.Disengage);
    }
}
