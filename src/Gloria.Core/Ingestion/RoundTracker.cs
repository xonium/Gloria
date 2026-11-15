namespace Gloria.Core.Ingestion;

/// <summary>Turns round events into live rounds, dropping warmup and restarted rounds.</summary>
public sealed class RoundTracker
{
    private readonly List<RoundInfo> _rounds = [];
    private (int Start, int? FreezeEnd, bool Warmup)? _current;
    private int? _lastFreezeEnd;

    public IReadOnlyList<RoundInfo> Rounds => _rounds;

    /// <summary>
    /// The match went live: rounds before this were warmup or restarts. The game announces it during the first
    /// live round (at its freeze end in the demos seen so far), so that round is kept and counts as live.
    /// A demo can also begin inside that round, with no round start recorded; it then starts here.
    /// </summary>
    public void MatchStart(int tick)
    {
        _rounds.Clear();
        _current = _current is { } c
            ? c with { Warmup = false }
            : (tick, _lastFreezeEnd == tick ? tick : null, false);
    }

    public void Start(int tick, bool warmup) => _current = (tick, null, warmup);

    public void FreezeEnd(int tick)
    {
        _lastFreezeEnd = tick;
        if (_current is { } c)
        {
            _current = c with { FreezeEnd = tick };
        }
    }

    public void End(int tick, Team winner)
    {
        if (_current is { Warmup: false } c)
        {
            _rounds.Add(new RoundInfo(_rounds.Count + 1, c.Start, c.FreezeEnd ?? c.Start, tick, winner));
        }

        _current = null;
    }
}
