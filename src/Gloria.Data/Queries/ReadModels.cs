using Gloria.Core.Analysis;
using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Weapons;
using Gloria.Data.Entities;

namespace Gloria.Data.Queries;

/// <summary>A player seen in the imported demos, for choosing who to track.</summary>
public sealed record PlayerCandidate(ulong SteamId, string Name, int DemoCount, IReadOnlyList<string> OtherNames);

/// <summary>One demo as seen by one player.</summary>
/// <param name="StartingSide">'T', 'C' or '-' in the first live round.</param>
/// <param name="DuelKills">Encounters the player won by killing the opponent.</param>
/// <param name="DuelDeaths">Encounters the player lost by dying.</param>
/// <param name="FirstShotKnown">Encounters where it is known whether the first shot hit.</param>
/// <param name="MainHabit">The aim category flagged most often in this demo, if any.</param>
/// <param name="IsCurrentVersion">Analyzed by the current analysis version.</param>
/// <param name="Kills">Scoreboard kills, from every death in the demo; null when imported before they were recorded.</param>
/// <param name="Damage">Health taken from enemies, utility included; null when imported before it was recorded.</param>
/// <param name="RoundsPlayed">Live rounds in which the player's side is known.</param>
public sealed record MatchSummary(
    int DemoId,
    string MapName,
    string FileName,
    DateTimeOffset? PlayedAt,
    PlayedAtSource PlayedAtSource,
    int RoundsWon,
    int RoundsLost,
    char StartingSide,
    int Encounters,
    int DuelKills,
    int DuelDeaths,
    int FirstShotHits,
    int FirstShotKnown,
    AimCategory? MainHabit,
    bool IsCurrentVersion,
    string ContentHash,
    int? Kills,
    int? Deaths,
    int? Damage,
    int RoundsPlayed)
{
    /// <summary>Kills per death; the kills alone when the player never died. Null without scoreboard numbers.</summary>
    public double? KillDeathRatio => Kills is { } k && Deaths is { } d ? (double)k / Math.Max(d, 1) : null;

    /// <summary>Average damage per round played. Null without scoreboard numbers or rounds.</summary>
    public double? DamagePerRound => Damage is { } dmg && RoundsPlayed > 0 ? (double)dmg / RoundsPlayed : null;
}

/// <param name="Won">Null when the player's side in the round is unknown.</param>
public sealed record RoundSummary(int Number, char Side, bool? Won, IReadOnlyList<EncounterOutcome> Outcomes);

/// <param name="SecondsIntoRound">From the end of freeze time.</param>
public sealed record EncounterRow(
    int Id,
    int DemoId,
    string MapName,
    int RoundNumber,
    double SecondsIntoRound,
    string OpponentName,
    string Weapon,
    string OpponentWeapon,
    EncounterOutcome Outcome,
    double DurationSeconds,
    double? TimeToFirstShot,
    float ErrorAtFirstShot,
    float SpeedRatio,
    int Flags,
    float HabitScore)
{
    public bool Has(AimCategory category) => (Flags & (1 << (int)category)) != 0;
}

public sealed record OpponentSummary(string Name, int Encounters, int Won, int Lost);

public sealed record MatchDetail(
    MatchSummary Summary,
    int TickRate,
    string AnalysisVersionKey,
    IReadOnlyList<RoundSummary> Rounds,
    IReadOnlyList<EncounterRow> Encounters,
    IReadOnlyList<OpponentSummary> Opponents);

/// <summary>Everything the encounter screen shows.</summary>
public sealed record EncounterDetail(
    EncounterRecord Record,
    string MapName,
    DateTimeOffset? PlayedAt,
    string OpponentName,
    int TickRate,
    double SecondsIntoRound,
    EncounterTrace? Trace,
    int? PreviousId,
    int? NextId);

/// <summary>Counts over a set of one player's encounters.</summary>
/// <param name="MovingFirstShots">First shots above the weapon's accurate speed.</param>
/// <param name="HabitCounts">Encounters flagged per <see cref="AimCategory"/>, indexed by the enum value.</param>
/// <param name="Clean">Encounters with no habit flagged.</param>
/// <param name="MedianPreAimError">Median crosshair error when the flick to the first shot began, in degrees.</param>
/// <param name="PreAimed">Encounters with none of the flick categories flagged.</param>
public sealed record EncounterStats(
    int Encounters,
    int Kills,
    int Deaths,
    int FirstShotHits,
    int FirstShotKnown,
    double? MedianTimeToFirstShot,
    int MovingFirstShots,
    int[] HabitCounts,
    int Clean,
    double? MedianPreAimError,
    int PreAimed);

/// <summary>The player's numbers with one class of weapon (what they fired first with).</summary>
public sealed record WeaponClassStats(WeaponClass Class, EncounterStats Stats);

/// <summary>Progress over the latest demos analyzed by the current version.</summary>
/// <param name="Demos">Oldest first.</param>
/// <param name="DeathIds">Encounters in those demos that the player lost, for explaining with the outcome model.</param>
/// <param name="OlderVersionDemos">Demos left out because an older analysis version produced them.</param>
public sealed record ProgressData(
    IReadOnlyList<DemoStats> Demos,
    IReadOnlyList<WeaponClassStats> WeaponClasses,
    IReadOnlyList<int> DeathIds,
    int OlderVersionDemos);

/// <summary>One demo's numbers for one player, for trends.</summary>
/// <param name="HabitCounts">Encounters flagged per <see cref="AimCategory"/>, indexed by the enum value.</param>
/// <param name="MedianPreAimError">Median crosshair error when the flick to the first shot began, in degrees.</param>
/// <param name="PreAimed">Encounters with none of the flick categories flagged.</param>
public sealed record DemoStats(
    int DemoId,
    string MapName,
    DateTimeOffset? PlayedAt,
    int Encounters,
    int Kills,
    int Deaths,
    int FirstShotHits,
    int FirstShotKnown,
    double? MedianTimeToFirstShot,
    int MovingFirstShots,
    int[] HabitCounts,
    int Clean,
    double? MedianPreAimError,
    int PreAimed)
{
    /// <summary>The same counts as <see cref="EncounterStats"/>, for <see cref="Comparison.Metrics"/>.</summary>
    public EncounterStats Stats => new(Encounters, Kills, Deaths, FirstShotHits, FirstShotKnown, MedianTimeToFirstShot,
        MovingFirstShots, HabitCounts, Clean, MedianPreAimError, PreAimed);
}

/// <param name="Recent">The latest demos, oldest first.</param>
/// <param name="Previous">The demos before <paramref name="Recent"/>, oldest first.</param>
/// <param name="OlderVersionDemos">Demos left out because an older analysis version produced them.</param>
public sealed record OverviewData(
    int DemoCount,
    IReadOnlyList<DemoStats> Recent,
    IReadOnlyList<DemoStats> Previous,
    IReadOnlyList<MatchSummary> RecentMatches,
    IReadOnlyList<EncounterRow> WorthALook,
    int OlderVersionDemos);
