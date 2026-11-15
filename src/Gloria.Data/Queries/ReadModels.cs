using Gloria.Core.Analysis;
using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Data.Entities;

namespace Gloria.Data.Queries;

/// <summary>A player seen in the imported demos, for choosing who to track.</summary>
public sealed record PlayerCandidate(ulong SteamId, string Name, int DemoCount, IReadOnlyList<string> OtherNames);

/// <summary>One demo as seen by one player.</summary>
/// <param name="StartingSide">'T', 'C' or '-' in the first live round.</param>
/// <param name="FirstShotKnown">Encounters where it is known whether the first shot hit.</param>
/// <param name="MainHabit">The aim category flagged most often in this demo, if any.</param>
/// <param name="IsCurrentVersion">Analyzed by the current analysis version.</param>
public sealed record MatchSummary(
    int DemoId,
    string MapName,
    string FileName,
    DateTimeOffset? PlayedAt,
    int RoundsWon,
    int RoundsLost,
    char StartingSide,
    int Encounters,
    int Kills,
    int Deaths,
    int FirstShotHits,
    int FirstShotKnown,
    AimCategory? MainHabit,
    bool IsCurrentVersion,
    string ContentHash);

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

/// <summary>One demo's numbers for one player, for trends.</summary>
/// <param name="HabitCounts">Encounters flagged per <see cref="AimCategory"/>, indexed by the enum value.</param>
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
    int[] HabitCounts);

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
