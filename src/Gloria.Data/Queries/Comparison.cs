using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Weapons;
using Gloria.Data.Entities;

namespace Gloria.Data.Queries;

public enum ComparisonKind
{
    /// <summary>The profile being viewed.</summary>
    You,

    /// <summary>A reference player bundled with the app.</summary>
    Pro,

    /// <summary>Another tracked profile, from the user's own demos.</summary>
    Friend,
}

/// <summary>One side of a comparison: a player's encounters over some demos, with their numbers overall and per weapon class.</summary>
/// <param name="Demos">Demos the encounters come from.</param>
/// <param name="Encounters">Without traces; what the stats and the models need.</param>
/// <param name="Note">Something to say about where the numbers come from, e.g. an older analysis.</param>
public sealed record ComparisonSide(
    string Name,
    ComparisonKind Kind,
    int Demos,
    EncounterStats Overall,
    IReadOnlyList<WeaponClassStats> ByClass,
    IReadOnlyList<EncounterRecord> Encounters,
    string? Note = null)
{
    public static ComparisonSide From(string name, ComparisonKind kind, int demos, IReadOnlyList<EncounterRecord> encounters, string? note = null) => new(
        name,
        kind,
        demos,
        GloriaReadModel.Tally(encounters),
        encounters
            .GroupBy(e => StaticWeaponData.Instance.Classify(e.FirstShotWeapon))
            .Select(g => new WeaponClassStats(g.Key, GloriaReadModel.Tally(g)))
            .OrderByDescending(w => w.Stats.Encounters)
            .ToList(),
        encounters,
        note);

    /// <summary>The numbers with one class of weapon, or overall when <paramref name="weaponClass"/> is null.</summary>
    public EncounterStats StatsFor(WeaponClass? weaponClass) => weaponClass is not { } c
        ? Overall
        : ByClass.FirstOrDefault(w => w.Class == c)?.Stats ?? GloriaReadModel.Tally([]);

    /// <summary>The duels lost, with one class of weapon or with any when <paramref name="weaponClass"/> is null.</summary>
    public IReadOnlyList<EncounterRecord> LossesWith(WeaponClass? weaponClass) => Encounters
        .Where(e => e.Outcome == EncounterOutcome.Death)
        .Where(e => weaponClass is null || StaticWeaponData.Instance.Classify(e.FirstShotWeapon) == weaponClass)
        .ToList();
}

public enum MetricUnit
{
    /// <summary>A fraction of encounters or duels, 0..1.</summary>
    Share,

    Seconds,

    Degrees,
}

/// <summary>One number for two players.</summary>
/// <param name="Habit">The aim habit whose share this is; null for the headline numbers.</param>
/// <param name="DependsOnOpposition">
/// Whether the number says as much about the opponents as about the player (duels won, reaction time), so it
/// compares poorly between players who play in different matches.
/// </param>
/// <param name="Gap">
/// How much worse "you" are, comparable across metrics: positive when worse, negative when better, 0 when equal.
/// See <see cref="Comparison.Gaps"/>.
/// </param>
public sealed record MetricGap(string Name, AimCategory? Habit, MetricUnit Unit, bool HigherIsBetter, bool DependsOnOpposition, double You, double Them, double Gap);

public static class Comparison
{
    /// <summary>
    /// How much a relative difference in a time or an angle counts against a share: 10% slower counts like
    /// 3 points of a share. Relative differences count up to 100%, so a time twice as long counts like 30 points.
    /// </summary>
    public const double RelativeWeight = 0.3;

    private static readonly (string Name, AimCategory? Habit, MetricUnit Unit, bool HigherIsBetter, bool DependsOnOpposition, Func<EncounterStats, double> Value)[] Metrics =
    [
        ("Duels won", null, MetricUnit.Share, true, true, s => Ratio(s.Kills, s.Kills + s.Deaths)),
        ("First-shot hit", null, MetricUnit.Share, true, false, s => Ratio(s.FirstShotHits, s.FirstShotKnown)),
        ("Median time to first shot", null, MetricUnit.Seconds, false, true, s => s.MedianTimeToFirstShot ?? double.NaN),
        ("Moving first shots", null, MetricUnit.Share, false, false, s => Ratio(s.MovingFirstShots, s.Encounters)),
        ("Clean encounters", null, MetricUnit.Share, true, false, s => Ratio(s.Clean, s.Encounters)),
        ("Pre-aimed", null, MetricUnit.Share, true, false, s => Ratio(s.PreAimed, s.Encounters)),
        ("Median pre-aim error", null, MetricUnit.Degrees, false, false, s => s.MedianPreAimError ?? double.NaN),
        .. Enum.GetValues<AimCategory>()
            // Moving shot is the moving-first-shots line above, unless corrections change it.
            .Where(c => c != AimCategory.MovingShot)
            .Select(c => (c.ToString(), (AimCategory?)c, MetricUnit.Share, false, c == AimCategory.SlowReaction, (Func<EncounterStats, double>)(s => Ratio(s.HabitCounts[(int)c], s.Encounters)))),
    ];

    /// <summary>
    /// Every metric for both players, in a fixed order, leaving out those either player has no value for.
    /// For shares the gap is the difference in points (0.05 for 5 points); for times and angles it is the
    /// relative difference times <see cref="RelativeWeight"/>, so gaps can be ranked against each other.
    /// </summary>
    public static IReadOnlyList<MetricGap> Gaps(EncounterStats you, EncounterStats them)
    {
        var gaps = new List<MetricGap>();
        foreach (var (name, habit, unit, higherIsBetter, opposition, value) in Metrics)
        {
            var a = value(you);
            var b = value(them);
            if (double.IsNaN(a) || double.IsNaN(b))
                continue;

            var worse = higherIsBetter ? b - a : a - b;
            var gap = unit == MetricUnit.Share ? worse : b > 0 ? Math.Clamp(worse / b, -1, 1) * RelativeWeight : 0;
            gaps.Add(new MetricGap(name, habit, unit, higherIsBetter, opposition, a, b, gap));
        }

        return gaps;
    }

    private static double Ratio(int a, int b) => b == 0 ? double.NaN : (double)a / b;
}
