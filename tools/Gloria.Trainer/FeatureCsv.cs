using System.Globalization;
using System.Text;
using System.Text.Json;
using Gloria.Core.Analysis;
using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Weapons;

namespace Gloria.Trainer;

/// <summary>One cached encounter: ids, the features, diagnostics and the outcome labels.</summary>
public sealed record FeatureRow(
    string DemoHash,
    string Map,
    int Round,
    ulong Player,
    ulong Opponent,
    int StartTick,
    int EndTick,
    int FirstShotTick,
    EncounterOutcome Outcome,
    string Weapon,
    EncounterFeatures Features,
    float ErrorAtFirstShotTick,
    float ErrorAtTickBeforeFirstShot,
    float ErrorTwoTicksBeforeFirstShot,
    int ApproachStartTick,
    int Shots,
    OutcomeLabels Labels);

/// <summary>Per-demo extraction statistics, written next to the CSV.</summary>
public sealed record DemoSummary(
    string DemoHash,
    string FileName,
    string Map,
    int Rounds,
    int TrackedPlayers,
    int Encounters,
    int DroppedWithoutShots,
    int DroppedDeathsWithoutShots,
    int DroppedUnseen,
    double ParseSeconds,
    double AnalyzeSeconds,
    IReadOnlyDictionary<int, int> SpottedRunLengths);

public static class FeatureCsv
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Columns in file order. Rows are read back by column name, so columns can be added freely.</summary>
    private static readonly (string Name, Func<FeatureRow, string> Value)[] Columns =
    [
        ("DemoHash", r => r.DemoHash),
        ("Map", r => r.Map),
        ("Round", r => I(r.Round)),
        ("Player", r => r.Player.ToString(Invariant)),
        ("Opponent", r => r.Opponent.ToString(Invariant)),
        ("StartTick", r => I(r.StartTick)),
        ("EndTick", r => I(r.EndTick)),
        ("FirstShotTick", r => I(r.FirstShotTick)),
        ("Outcome", r => r.Outcome.ToString()),
        ("Weapon", r => r.Weapon),
        (nameof(EncounterFeatures.TimeToFirstShot), r => F(r.Features.TimeToFirstShot)),
        (nameof(EncounterFeatures.TimeToOnTarget), r => r.Features.TimeToOnTarget is { } t ? F(t) : ""),
        (nameof(EncounterFeatures.AngularErrorAtFirstVisible), r => F(r.Features.AngularErrorAtFirstVisible)),
        (nameof(EncounterFeatures.AngularErrorAtFirstShot), r => F(r.Features.AngularErrorAtFirstShot)),
        (nameof(EncounterFeatures.PeakOvershootDegrees), r => F(r.Features.PeakOvershootDegrees)),
        (nameof(EncounterFeatures.CorrectionCount), r => I(r.Features.CorrectionCount)),
        (nameof(EncounterFeatures.FlickDistanceDegrees), r => F(r.Features.FlickDistanceDegrees)),
        (nameof(EncounterFeatures.SpeedRatioAtFirstShot), r => F(r.Features.SpeedRatioAtFirstShot)),
        (nameof(EncounterFeatures.ShotsWhileMovingRatio), r => F(r.Features.ShotsWhileMovingRatio)),
        (nameof(EncounterFeatures.DistanceToEnemy), r => F(r.Features.DistanceToEnemy)),
        (nameof(EncounterFeatures.WeaponClass), r => r.Features.WeaponClass.ToString()),
        (nameof(EncounterFeatures.IsKill), r => B(r.Features.IsKill)),
        (nameof(EncounterFeatures.PlayerHealth), r => I(r.Features.PlayerHealth)),
        (nameof(EncounterFeatures.OpponentHealth), r => I(r.Features.OpponentHealth)),
        (nameof(EncounterFeatures.OpponentWeaponClass), r => r.Features.OpponentWeaponClass.ToString()),
        (nameof(EncounterFeatures.FiredFirst), r => B(r.Features.FiredFirst)),
        (nameof(EncounterFeatures.PreAimErrorDegrees), r => F(r.Features.PreAimErrorDegrees)),
        ("ErrorAtFirstShotTick", r => F(r.ErrorAtFirstShotTick)),
        ("ErrorAtTickBeforeFirstShot", r => F(r.ErrorAtTickBeforeFirstShot)),
        ("ErrorTwoTicksBeforeFirstShot", r => F(r.ErrorTwoTicksBeforeFirstShot)),
        ("ApproachStartTick", r => I(r.ApproachStartTick)),
        ("Shots", r => I(r.Shots)),
        (nameof(OutcomeLabels.FirstShotHit), r => B(r.Labels.FirstShotHit)),
        (nameof(OutcomeLabels.WonDuel), r => B(r.Labels.WonDuel)),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string CsvPath(TrainerOptions options, string hash) =>
        Path.Combine(options.FeatureDirectory, $"{hash}.{TrainerOptions.AnalysisVersion}.csv");

    public static string SummaryPath(TrainerOptions options, string hash) =>
        Path.Combine(options.FeatureDirectory, $"{hash}.{TrainerOptions.AnalysisVersion}.json");

    public static FeatureRow ToRow(string hash, string map, AnalyzedEncounter a) => new(
        hash,
        map,
        a.Encounter.RoundNumber,
        a.Encounter.PlayerSteamId,
        a.Encounter.OpponentSteamId,
        a.Encounter.StartTick,
        a.Encounter.EndTick,
        a.Metrics.FirstShotTick,
        a.Encounter.Outcome,
        a.Metrics.FirstShotWeapon,
        a.Features,
        a.Metrics.AngularErrorAtFirstShotTick,
        a.Metrics.AngularErrorAtTickBeforeFirstShot,
        a.Metrics.AngularErrorTwoTicksBeforeFirstShot,
        a.Metrics.ApproachStartTick,
        a.Metrics.ShotTicks.Count,
        a.Labels);

    /// <summary>Writes to a temporary file first, so an interrupted run never leaves a partial cache entry.</summary>
    public static void Write(string path, IEnumerable<FeatureRow> rows, DemoSummary summary, string summaryPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
        {
            writer.WriteLine(string.Join(',', Columns.Select(c => c.Name)));
            foreach (var r in rows)
                writer.WriteLine(string.Join(',', Columns.Select(c => c.Value(r))));
        }

        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>All rows cached for the current analysis version, plus each demo's summary.</summary>
    public static (List<FeatureRow> Rows, List<DemoSummary> Demos) ReadAll(TrainerOptions options)
    {
        var suffix = $".{TrainerOptions.AnalysisVersion}.csv";
        var files = Directory.Exists(options.FeatureDirectory)
            ? Directory.GetFiles(options.FeatureDirectory, "*" + suffix).Order(StringComparer.Ordinal).ToList()
            : [];
        if (files.Count == 0)
            throw new TrainerException($"No cached features for analysis version {TrainerOptions.AnalysisVersion} in {options.FeatureDirectory}. Run 'extract' first.");

        var rows = files.SelectMany(Read).ToList();
        var demos = files
            .Select(f => f[..^".csv".Length] + ".json")
            .Where(File.Exists)
            .Select(f => JsonSerializer.Deserialize<DemoSummary>(File.ReadAllText(f))!)
            .ToList();
        return (rows, demos);
    }

    private static IEnumerable<FeatureRow> Read(string path)
    {
        using var lines = File.ReadLines(path).GetEnumerator();
        if (!lines.MoveNext())
            yield break;

        var index = lines.Current.Split(',').Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i);
        while (lines.MoveNext())
        {
            var cells = lines.Current.Split(',');
            string C(string name) => cells[index[name]];
            var features = new EncounterFeatures(
                TimeToFirstShot: PF(C(nameof(EncounterFeatures.TimeToFirstShot))),
                TimeToOnTarget: C(nameof(EncounterFeatures.TimeToOnTarget)) is { Length: > 0 } t ? PF(t) : null,
                AngularErrorAtFirstVisible: PF(C(nameof(EncounterFeatures.AngularErrorAtFirstVisible))),
                AngularErrorAtFirstShot: PF(C(nameof(EncounterFeatures.AngularErrorAtFirstShot))),
                PeakOvershootDegrees: PF(C(nameof(EncounterFeatures.PeakOvershootDegrees))),
                CorrectionCount: PI(C(nameof(EncounterFeatures.CorrectionCount))),
                FlickDistanceDegrees: PF(C(nameof(EncounterFeatures.FlickDistanceDegrees))),
                SpeedRatioAtFirstShot: PF(C(nameof(EncounterFeatures.SpeedRatioAtFirstShot))),
                ShotsWhileMovingRatio: PF(C(nameof(EncounterFeatures.ShotsWhileMovingRatio))),
                DistanceToEnemy: PF(C(nameof(EncounterFeatures.DistanceToEnemy))),
                WeaponClass: Enum.Parse<WeaponClass>(C(nameof(EncounterFeatures.WeaponClass))),
                IsKill: C(nameof(EncounterFeatures.IsKill)) == "1",
                PlayerHealth: PI(C(nameof(EncounterFeatures.PlayerHealth))),
                OpponentHealth: PI(C(nameof(EncounterFeatures.OpponentHealth))),
                OpponentWeaponClass: Enum.Parse<WeaponClass>(C(nameof(EncounterFeatures.OpponentWeaponClass))),
                FiredFirst: C(nameof(EncounterFeatures.FiredFirst)) == "1",
                PreAimErrorDegrees: PF(C(nameof(EncounterFeatures.PreAimErrorDegrees))));
            yield return new FeatureRow(
                C("DemoHash"), C("Map"), PI(C("Round")),
                ulong.Parse(C("Player"), Invariant), ulong.Parse(C("Opponent"), Invariant),
                PI(C("StartTick")), PI(C("EndTick")), PI(C("FirstShotTick")),
                Enum.Parse<EncounterOutcome>(C("Outcome")), C("Weapon"),
                features,
                PF(C("ErrorAtFirstShotTick")), PF(C("ErrorAtTickBeforeFirstShot")), PF(C("ErrorTwoTicksBeforeFirstShot")),
                PI(C("ApproachStartTick")), PI(C("Shots")),
                new OutcomeLabels(PB(C(nameof(OutcomeLabels.FirstShotHit))), PB(C(nameof(OutcomeLabels.WonDuel)))));
        }
    }

    private static string I(int v) => v.ToString(Invariant);
    private static string F(float v) => float.IsNaN(v) ? "" : v.ToString("0.####", Invariant);
    private static string B(bool v) => v ? "1" : "0";
    private static string B(bool? v) => v switch { true => "1", false => "0", null => "" };
    private static int PI(string s) => int.Parse(s, Invariant);
    private static float PF(string s) => s.Length == 0 ? float.NaN : float.Parse(s, Invariant);
    private static bool? PB(string s) => s switch { "1" => true, "0" => false, _ => null };
}
