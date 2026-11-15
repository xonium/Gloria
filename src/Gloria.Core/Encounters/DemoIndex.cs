using System.Runtime.CompilerServices;
using Gloria.Core.Ingestion;

namespace Gloria.Core.Encounters;

/// <summary>
/// Events of one demo grouped by player and sorted by tick, plus the shot attribution.
/// Built once per <see cref="ParsedDemo"/> and shared by the segmenter, metrics and labeler.
/// </summary>
public sealed class DemoIndex
{
    private static readonly ConditionalWeakTable<ParsedDemo, DemoIndex> Cache = new();

    private readonly ParsedDemo _demo;
    private readonly ILookup<ulong, WeaponFireEvent> _firesByShooter;
    private readonly ILookup<ulong, PlayerHurtEvent> _hurtsByAttacker;
    private readonly ILookup<ulong, PlayerHurtEvent> _hurtsByVictim;
    private readonly ILookup<ulong, PlayerDeathEvent> _deathsByVictim;
    private readonly Dictionary<ulong, IReadOnlyList<AttributedShot>> _shots = [];
    private readonly Lock _lock = new();

    private DemoIndex(ParsedDemo demo)
    {
        _demo = demo;
        var events = demo.Events.OrderBy(e => e.Tick).ToList();
        _firesByShooter = events.OfType<WeaponFireEvent>().ToLookup(e => e.ShooterSteamId);
        _hurtsByAttacker = events.OfType<PlayerHurtEvent>().Where(e => e.AttackerSteamId is not null).ToLookup(e => e.AttackerSteamId!.Value);
        _hurtsByVictim = events.OfType<PlayerHurtEvent>().ToLookup(e => e.VictimSteamId);
        _deathsByVictim = events.OfType<PlayerDeathEvent>().ToLookup(e => e.VictimSteamId);
    }

    public static DemoIndex For(ParsedDemo demo) => Cache.GetValue(demo, d => new DemoIndex(d));

    public IEnumerable<WeaponFireEvent> FiresBy(ulong shooter) => _firesByShooter[shooter];

    public IEnumerable<PlayerHurtEvent> HurtsBy(ulong attacker) => _hurtsByAttacker[attacker];

    public IEnumerable<PlayerHurtEvent> HurtsOf(ulong victim) => _hurtsByVictim[victim];

    /// <summary>The victim's death within [<paramref name="fromTick"/>, <paramref name="toTick"/>], if any.</summary>
    public PlayerDeathEvent? DeathOf(ulong victim, int fromTick, int toTick) =>
        _deathsByVictim[victim].FirstOrDefault(d => d.Tick >= fromTick && d.Tick <= toTick);

    /// <summary>Every gun shot by the player with the opponent it was aimed at, sorted by tick.</summary>
    public IReadOnlyList<AttributedShot> ShotsBy(ulong shooter)
    {
        lock (_lock)
        {
            if (!_shots.TryGetValue(shooter, out var shots))
            {
                shots = ShotAttribution.Attribute(_demo, this, shooter);
                _shots[shooter] = shots;
            }

            return shots;
        }
    }
}
