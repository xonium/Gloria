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

    private static readonly string[] Header =
    [
        "DemoHash", "Map", "Round", "Player", "Opponent", "StartTick", "EndTick", "FirstShotTick", "Outcome", "Weapon",
        nameof(EncounterFeatures.TimeToFirstShot), nameof(EncounterFeatures.TimeToOnTarget),
        nameof(EncounterFeatures.AngularErrorAtFirstVisible), nameof(EncounterFeatures.AngularErrorAtFirstShot),
        nameof(EncounterFeatures.PeakOvershootDegrees), nameof(EncounterFeatures.CorrectionCount),
        nameof(EncounterFeatures.FlickDistanceDegrees), nameof(EncounterFeatures.SpeedRatioAtFirstShot),
        nameof(EncounterFeatures.ShotsWhileMovingRatio), nameof(EncounterFeatures.DistanceToEnemy),
        nameof(EncounterFeatures.WeaponClass), nameof(EncounterFeatures.IsKill),
        "ErrorAtFirstShotTick", "ErrorAtTickBeforeFirstShot", "ErrorTwoTicksBeforeFirstShot", "ApproachStartTick", "Shots",
        nameof(OutcomeLabels.FirstShotHit), nameof(OutcomeLabels.WonDuel),
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
            writer.WriteLine(string.Join(',', Header));
            foreach (var r in rows)
                writer.WriteLine(Format(r));
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

        var rows = files.SelectMany(f => File.ReadLines(f).Skip(1).Select(Parse)).ToList();
        var demos = files
            .Select(f => f[..^".csv".Length] + ".json")
            .Where(File.Exists)
            .Select(f => JsonSerializer.Deserialize<DemoSummary>(File.ReadAllText(f))!)
            .ToList();
        return (rows, demos);
    }

    private static string Format(FeatureRow r)
    {
        var f = r.Features;
        string[] values =
        [
            r.DemoHash, r.Map, I(r.Round), r.Player.ToString(Invariant), r.Opponent.ToString(Invariant),
            I(r.StartTick), I(r.EndTick), I(r.FirstShotTick), r.Outcome.ToString(), r.Weapon,
            F(f.TimeToFirstShot), f.TimeToOnTarget is { } t ? F(t) : "",
            F(f.AngularErrorAtFirstVisible), F(f.AngularErrorAtFirstShot),
            F(f.PeakOvershootDegrees), I(f.CorrectionCount),
            F(f.FlickDistanceDegrees), F(f.SpeedRatioAtFirstShot),
            F(f.ShotsWhileMovingRatio), F(f.DistanceToEnemy),
            f.WeaponClass.ToString(), f.IsKill ? "1" : "0",
            F(r.ErrorAtFirstShotTick), F(r.ErrorAtTickBeforeFirstShot), F(r.ErrorTwoTicksBeforeFirstShot), I(r.ApproachStartTick), I(r.Shots),
            B(r.Labels.FirstShotHit), B(r.Labels.WonDuel),
        ];
        return string.Join(',', values);

        static string I(int v) => v.ToString(Invariant);
        static string F(float v) => float.IsNaN(v) ? "" : v.ToString("0.####", Invariant);
        static string B(bool? v) => v switch { true => "1", false => "0", null => "" };
    }

    private static FeatureRow Parse(string line)
    {
        var c = line.Split(',');
        var features = new EncounterFeatures(
            TimeToFirstShot: F(c[10]),
            TimeToOnTarget: c[11].Length == 0 ? null : F(c[11]),
            AngularErrorAtFirstVisible: F(c[12]),
            AngularErrorAtFirstShot: F(c[13]),
            PeakOvershootDegrees: F(c[14]),
            CorrectionCount: I(c[15]),
            FlickDistanceDegrees: F(c[16]),
            SpeedRatioAtFirstShot: F(c[17]),
            ShotsWhileMovingRatio: F(c[18]),
            DistanceToEnemy: F(c[19]),
            WeaponClass: Enum.Parse<WeaponClass>(c[20]),
            IsKill: c[21] == "1");
        return new FeatureRow(
            c[0], c[1], I(c[2]), ulong.Parse(c[3], Invariant), ulong.Parse(c[4], Invariant),
            I(c[5]), I(c[6]), I(c[7]), Enum.Parse<EncounterOutcome>(c[8]), c[9],
            features,
            F(c[22]), F(c[23]), F(c[24]), I(c[25]), I(c[26]),
            new OutcomeLabels(B(c[27]), B(c[28])));

        static int I(string s) => int.Parse(s, Invariant);
        static float F(string s) => s.Length == 0 ? float.NaN : float.Parse(s, Invariant);
        static bool? B(string s) => s switch { "1" => true, "0" => false, _ => null };
    }
}
