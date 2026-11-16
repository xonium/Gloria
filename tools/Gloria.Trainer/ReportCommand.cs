using System.Globalization;
using System.Text;
using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Metrics;
using Gloria.Core.Weapons;
using Gloria.Ml;

namespace Gloria.Trainer;

/// <summary>
/// Writes report.md: dataset counts, label rates per feature bin, threshold scans for each rule
/// constant, the sub-tick alignment check, spotted cadence and the latest model metrics.
/// </summary>
public static class ReportCommand
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private sealed record FeatureSpec(string Name, Func<FeatureRow, float?> Value, float[] Edges);

    private static readonly FeatureSpec[] Features =
    [
        new(nameof(EncounterFeatures.AngularErrorAtFirstShot), r => r.Features.AngularErrorAtFirstShot, [0, 0.5f, 1, 1.5f, 2, 3, 5, 10]),
        new(nameof(EncounterFeatures.SpeedRatioAtFirstShot), r => r.Features.SpeedRatioAtFirstShot, [0, 0.25f, 0.5f, 0.75f, 1, 1.25f, 1.5f, 2, 2.5f]),
        new(nameof(EncounterFeatures.TimeToFirstShot), r => r.Features.TimeToFirstShot, [0, 0.05f, 0.1f, 0.2f, 0.3f, 0.4f, 0.6f, 0.8f, 1, 1.5f]),
        new(nameof(EncounterFeatures.TimeToOnTarget), r => r.Features.TimeToOnTarget, [0, 0.05f, 0.1f, 0.2f, 0.3f, 0.5f, 1]),
        new(nameof(EncounterFeatures.PeakOvershootDegrees), r => r.Features.PeakOvershootDegrees, [0, 0.01f, 0.5f, 1, 1.5f, 2, 3, 5]),
        new(nameof(EncounterFeatures.CorrectionCount), r => r.Features.CorrectionCount, [0, 1, 2, 3, 4]),
        new(nameof(EncounterFeatures.FlickDistanceDegrees), r => r.Features.FlickDistanceDegrees, [0, 0.5f, 2, 5, 10, 20, 45]),
        new(nameof(EncounterFeatures.PreAimErrorDegrees), r => r.Features.PreAimErrorDegrees, [0, 2, 5, 10, 15, 20, 30, 45, 90]),
        new(nameof(EncounterFeatures.AngularErrorAtFirstVisible), r => r.Features.AngularErrorAtFirstVisible, [0, 1, 2, 5, 10, 20, 45]),
        new(nameof(EncounterFeatures.ShotsWhileMovingRatio), r => r.Features.ShotsWhileMovingRatio, [0, 0.01f, 0.25f, 0.5f, 0.75f, 1]),
        new(nameof(EncounterFeatures.DistanceToEnemy), r => r.Features.DistanceToEnemy, [0, 250, 500, 1000, 1500, 2000, 3000]),
        new(nameof(EncounterFeatures.PlayerHealth), r => r.Features.PlayerHealth, [0, 25, 50, 75, 100]),
        new(nameof(EncounterFeatures.OpponentHealth), r => r.Features.OpponentHealth, [0, 25, 50, 75, 100]),
        new("HealthDifference (player − opponent)", r => r.Features.PlayerHealth - r.Features.OpponentHealth, [-100, -50, -25, -1, 1, 25, 50]),
        new(nameof(EncounterFeatures.FiredFirst), r => r.Features.FiredFirst ? 1 : 0, [0, 1]),
        new($"{nameof(EncounterFeatures.OpponentWeaponClass)} (0 Other, 1 Pistol, 2 Smg, 3 Rifle, 4 Sniper, 5 Shotgun, 6 MachineGun)",
            r => (float)r.Features.OpponentWeaponClass, [0, 1, 2, 3, 4, 5, 6]),
    ];

    public static void Run(TrainerOptions options)
    {
        var (rows, demos) = FeatureCsv.ReadAll(options);
        var md = new StringBuilder();
        var binsDirectory = Path.Combine(options.OutputDirectory, "bins");
        Directory.CreateDirectory(binsDirectory);

        md.AppendLine("# Training report");
        md.AppendLine();
        md.AppendLine($"Generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm}, analysis version `{TrainerOptions.AnalysisVersion}`.");
        md.AppendLine("Rates come with 95% Wilson intervals. FirstShotHit: the first shot at the opponent damaged them. WonDuel: kill rather than death.");
        md.AppendLine();

        Dataset(md, rows, demos);
        SpottedCadence(md, demos);
        SubTickAlignment(md, rows);
        ThresholdScans(md, rows);
        RulePrevalence(md, rows);
        FeatureBins(md, rows, binsDirectory);
        Models(md, options);

        var path = Path.Combine(options.OutputDirectory, "report.md");
        File.WriteAllText(path, md.ToString());
        Console.WriteLine($"report: wrote {Path.GetRelativePath(options.RepoRoot, path)}");
    }

    private static void Dataset(StringBuilder md, List<FeatureRow> rows, List<DemoSummary> demos)
    {
        md.AppendLine("## Dataset");
        md.AppendLine();
        md.AppendLine("| Demo | Map | Rounds | Encounters | Kill | Death | Disengage | Timeout | Dropped: no shot | of which deaths | Dropped: never seen |");
        md.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var d in demos)
        {
            var mine = rows.Where(r => r.DemoHash == d.DemoHash).ToList();
            int Count(EncounterOutcome o) => mine.Count(r => r.Outcome == o);
            md.AppendLine($"| {d.FileName} | {d.Map} | {d.Rounds} | {d.Encounters} | {Count(EncounterOutcome.Kill)} | {Count(EncounterOutcome.Death)} | " +
                          $"{Count(EncounterOutcome.Disengage)} | {Count(EncounterOutcome.Timeout)} | {d.DroppedWithoutShots} | {d.DroppedDeathsWithoutShots} | {d.DroppedUnseen} |");
        }

        var hit = rows.Where(r => r.Labels.FirstShotHit is not null).ToList();
        var duel = rows.Where(r => r.Labels.WonDuel is not null).ToList();
        md.AppendLine();
        md.AppendLine($"- {rows.Count} encounters from {demos.Count} demos.");
        md.AppendLine($"- FirstShotHit: {Rate(hit.Count(r => r.Labels.FirstShotHit == true), hit.Count)} of {hit.Count} labeled.");
        md.AppendLine($"- WonDuel: {Rate(duel.Count(r => r.Labels.WonDuel == true), duel.Count)} of {duel.Count} kills and deaths. " +
                      "Above 50% because deaths where the player never fired are dropped; see the 'no shot' columns.");
        md.AppendLine($"- Weapons: {string.Join(", ", rows.GroupBy(r => r.Features.WeaponClass).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}.");
        md.AppendLine();
    }

    private static void SpottedCadence(StringBuilder md, List<DemoSummary> demos)
    {
        var runs = demos.SelectMany(d => d.SpottedRunLengths)
            .GroupBy(kv => kv.Key)
            .Select(g => (Length: g.Key, Count: g.Sum(kv => kv.Value)))
            .OrderByDescending(r => r.Count)
            .Take(8)
            .ToList();
        md.AppendLine("## Spotted cadence");
        md.AppendLine();
        md.AppendLine("How many ticks the spotted state stays set at a time (most common lengths). Lengths in multiples of one");
        md.AppendLine("update interval mean visibility onsets are only known to within that interval, which limits the");
        md.AppendLine("precision of TimeToFirstShot and the other timings measured from first contact.");
        md.AppendLine();
        md.AppendLine(string.Join(", ", runs.Select(r => $"{r.Length} ticks: {r.Count}")));
        md.AppendLine();
    }

    private static void SubTickAlignment(StringBuilder md, List<FeatureRow> rows)
    {
        var labeled = rows.Where(r => r.Labels.FirstShotHit is not null).ToList();
        md.AppendLine("## Sub-tick alignment");
        md.AppendLine();
        md.AppendLine("Single-feature AUC of the crosshair error at the first shot for predicting FirstShotHit (lower error = hit),");
        md.AppendLine("measured at three moments. The highest AUC is the alignment that best matches where the shot really went.");
        md.AppendLine();
        md.AppendLine("| Error measured at | AUC |");
        md.AppendLine("|---|---:|");
        md.AppendLine($"| two ticks before the fire tick | {Auc(labeled, r => -r.ErrorTwoTicksBeforeFirstShot):0.000} |");
        md.AppendLine($"| tick before the fire tick | {Auc(labeled, r => -r.ErrorAtTickBeforeFirstShot):0.000} |");
        md.AppendLine($"| shot moment used for the features | {Auc(labeled, r => -r.Features.AngularErrorAtFirstShot):0.000} |");
        md.AppendLine($"| fire tick | {Auc(labeled, r => -r.ErrorAtFirstShotTick):0.000} |");
        md.AppendLine();
    }

    private static void ThresholdScans(StringBuilder md, List<FeatureRow> rows)
    {
        var hit = rows.Where(r => r.Labels.FirstShotHit is not null).ToList();
        var duel = rows.Where(r => r.Labels.WonDuel is not null).ToList();
        static IEnumerable<float> Steps(float from, float to, float step)
        {
            for (var v = from; v <= to + 1e-4f; v += step)
                yield return MathF.Round(v, 3);
        }

        md.AppendLine("## Threshold scans");
        md.AppendLine();
        md.AppendLine("For each rule constant in `RuleBasedAimLabeler`: the label rate below and above the current value, and the cut");
        md.AppendLine("with the largest chi-square (at least 50 rows on each side). A large chi-square at a cut far from the current");
        md.AppendLine("value suggests moving the constant.");
        md.AppendLine();
        md.AppendLine("| Rule | Feature → label | Rows | Current | Below | Above | Best cut | Below | Above | χ² |");
        md.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        void Scan(string rule, string what, List<FeatureRow> subset, Func<FeatureRow, float> value, Func<FeatureRow, bool> label, float current, IEnumerable<float> candidates)
        {
            var pairs = subset.Select(r => (value(r), label(r))).ToList();
            var now = FeatureBinReport.BestThreshold(pairs, [current], minPerSide: 1);
            var best = FeatureBinReport.BestThreshold(pairs, candidates);
            md.AppendLine($"| {rule} | {what} | {pairs.Count} | {current.ToString("0.##", Invariant)} | {P(now?.RateBelow)} | {P(now?.RateAbove)} | " +
                          $"{best?.Threshold.ToString(Invariant) ?? "–"} | {P(best?.RateBelow)} | {P(best?.RateAbove)} | {best?.ChiSquare.ToString("0.0", Invariant) ?? "–"} |");
        }

        bool Hit(FeatureRow r) => r.Labels.FirstShotHit == true;
        bool Won(FeatureRow r) => r.Labels.WonDuel == true;

        // The target's angular size shrinks with range, so the tolerance is in body half-widths.
        Scan("OnTargetBodyHalfWidths (Hasty)", "error / half-width angle → FirstShotHit", hit,
            r => r.Features.AngularErrorAtFirstShot / BodyModel.HalfWidthDegrees(r.Features.DistanceToEnemy),
            Hit, RuleBasedAimLabeler.OnTargetBodyHalfWidths, Steps(0.25f, 8, 0.25f));
        Scan("  … as a fixed angle", "AngularErrorAtFirstShot (°) → FirstShotHit", hit, r => r.Features.AngularErrorAtFirstShot, Hit,
            RuleBasedAimLabeler.OnTargetDegreesAt(1000), Steps(0.25f, 6, 0.25f));
        foreach (var (low, high) in new[] { (0f, 500f), (500f, 1000f), (1000f, 1500f), (1500f, 99999f) })
        {
            var mid = high > 9999 ? 2000f : (low + high) / 2;
            Scan($"  … fixed angle at {low:0}–{(high > 9999 ? "∞" : high.ToString("0", Invariant))} units", "AngularErrorAtFirstShot (°) → FirstShotHit",
                hit.Where(r => r.Features.DistanceToEnemy >= low && r.Features.DistanceToEnemy < high).ToList(),
                r => r.Features.AngularErrorAtFirstShot, Hit, MathF.Round(RuleBasedAimLabeler.OnTargetDegreesAt(mid), 2), Steps(0.25f, 6, 0.25f));
        }
        Scan("MovingSpeedRatio (MovingShot)", "SpeedRatioAtFirstShot → FirstShotHit", hit, r => r.Features.SpeedRatioAtFirstShot, Hit,
            RuleBasedAimLabeler.MovingSpeedRatio, Steps(0.1f, 3, 0.1f));
        Scan("  … with error < 2°", "SpeedRatioAtFirstShot → FirstShotHit", hit.Where(r => r.Features.AngularErrorAtFirstShot < 2).ToList(),
            r => r.Features.SpeedRatioAtFirstShot, Hit, RuleBasedAimLabeler.MovingSpeedRatio, Steps(0.1f, 3, 0.1f));
        Scan("  … rifles, error < 2°", "SpeedRatioAtFirstShot → FirstShotHit",
            hit.Where(r => r.Features.AngularErrorAtFirstShot < 2 && r.Features.WeaponClass == WeaponClass.Rifle).ToList(),
            r => r.Features.SpeedRatioAtFirstShot, Hit, RuleBasedAimLabeler.MovingSpeedRatio, Steps(0.1f, 3, 0.1f));
        Scan("SlowReactionSeconds", "TimeToFirstShot → WonDuel", duel, r => r.Features.TimeToFirstShot, Won,
            RuleBasedAimLabeler.SlowReactionSeconds, Steps(0.05f, 1.5f, 0.05f));
        Scan("OvershootDegrees", "PeakOvershootDegrees → FirstShotHit", hit, r => r.Features.PeakOvershootDegrees, Hit,
            RuleBasedAimLabeler.OvershootDegrees, Steps(0.25f, 6, 0.25f));
        Scan("OvershootDegrees", "PeakOvershootDegrees → WonDuel", duel, r => r.Features.PeakOvershootDegrees, Won,
            RuleBasedAimLabeler.OvershootDegrees, Steps(0.25f, 6, 0.25f));
        Scan("UndershootCorrections", "CorrectionCount → FirstShotHit", hit, r => r.Features.CorrectionCount, Hit,
            RuleBasedAimLabeler.UndershootCorrections, Steps(1, 5, 1));
        Scan("UndershootCorrections", "CorrectionCount → WonDuel", duel, r => r.Features.CorrectionCount, Won,
            RuleBasedAimLabeler.UndershootCorrections, Steps(1, 5, 1));
        // Each flick cut is scanned among the encounters above the previous one, so each scan finds its own band's edge.
        foreach (var (rule, current, above) in new[]
                 {
                     ("SmallFlickDegrees", RuleBasedAimLabeler.SmallFlickDegrees, 0f),
                     ("MediumFlickDegrees", RuleBasedAimLabeler.MediumFlickDegrees, RuleBasedAimLabeler.SmallFlickDegrees),
                     ("LargeFlickDegrees", RuleBasedAimLabeler.LargeFlickDegrees, RuleBasedAimLabeler.MediumFlickDegrees),
                 })
        {
            var where = above > 0 ? $" (of those ≥ {above:0}°)" : "";
            Scan(rule, $"PreAimErrorDegrees → FirstShotHit{where}", hit.Where(r => r.Features.PreAimErrorDegrees >= above).ToList(),
                r => r.Features.PreAimErrorDegrees, Hit, current, Steps(above + 1, 90, 1));
            Scan(rule, $"PreAimErrorDegrees → WonDuel{where}", duel.Where(r => r.Features.PreAimErrorDegrees >= above).ToList(),
                r => r.Features.PreAimErrorDegrees, Won, current, Steps(above + 1, 90, 1));
        }
        md.AppendLine();
    }

    private static void RulePrevalence(StringBuilder md, List<FeatureRow> rows)
    {
        md.AppendLine("## Rule prevalence");
        md.AppendLine();
        md.AppendLine("Share of encounters each rule flags with the current constants, and the outcome rates of flagged vs other encounters.");
        md.AppendLine();
        md.AppendLine("| Category | Flagged | FirstShotHit flagged / other | WonDuel flagged / other |");
        md.AppendLine("|---|---:|---:|---:|");
        foreach (var category in Enum.GetValues<AimCategory>())
        {
            var flagged = rows.Where(r => RuleBasedAimLabeler.IsFlagged(r.Features, category)).ToList();
            var other = rows.Except(flagged).ToList();
            md.AppendLine($"| {category} | {Rate(flagged.Count, rows.Count)} | {HitRate(flagged)} / {HitRate(other)} | {WinRate(flagged)} / {WinRate(other)} |");
        }

        md.AppendLine();

        static string HitRate(List<FeatureRow> r) => P(RateOf(r, x => x.Labels.FirstShotHit));
        static string WinRate(List<FeatureRow> r) => P(RateOf(r, x => x.Labels.WonDuel));
    }

    private static void FeatureBins(StringBuilder md, List<FeatureRow> rows, string binsDirectory)
    {
        md.AppendLine("## Label rates per feature bin");
        md.AppendLine();
        md.AppendLine("Single-feature AUC is for predicting the label from the feature alone (0.5 = no signal; below 0.5 = higher values are worse).");
        md.AppendLine();

        foreach (var spec in Features)
        {
            var hits = rows.Where(r => r.Labels.FirstShotHit is not null && spec.Value(r) is not null)
                .Select(r => (spec.Value(r)!.Value, r.Labels.FirstShotHit!.Value)).ToList();
            var wins = rows.Where(r => r.Labels.WonDuel is not null && spec.Value(r) is not null)
                .Select(r => (spec.Value(r)!.Value, r.Labels.WonDuel!.Value)).ToList();
            var hitBins = FeatureBinReport.Fixed(hits, spec.Edges);
            var winBins = FeatureBinReport.Fixed(wins, spec.Edges);

            md.AppendLine($"### {spec.Name}");
            md.AppendLine();
            md.AppendLine($"Single-feature AUC: FirstShotHit {PairAuc(hits):0.000}, WonDuel {PairAuc(wins):0.000}.");
            md.AppendLine();
            md.AppendLine("| Bin | n | FirstShotHit | n | WonDuel |");
            md.AppendLine("|---|---:|---|---:|---|");
            var csv = new StringBuilder("lower,upper,hit_n,hit_rate,hit_low,hit_high,win_n,win_rate,win_low,win_high\n");
            for (var i = 0; i < hitBins.Count; i++)
            {
                var h = hitBins[i];
                var w = winBins[i];
                var upper = float.IsPositiveInfinity(h.Upper) ? "∞" : h.Upper.ToString(Invariant);
                md.AppendLine($"| [{h.Lower.ToString(Invariant)}, {upper}) | {h.Count} | {WithInterval(h)} | {w.Count} | {WithInterval(w)} |");
                csv.AppendLine(string.Join(',',
                    h.Lower.ToString(Invariant), h.Upper.ToString(Invariant),
                    h.Count, N(h.Rate), N(h.Interval.Low), N(h.Interval.High),
                    w.Count, N(w.Rate), N(w.Interval.Low), N(w.Interval.High)));
            }

            File.WriteAllText(Path.Combine(binsDirectory, $"{spec.Name.Split(' ')[0]}.csv"), csv.ToString());
            md.AppendLine();
        }

        static string N(double v) => double.IsFinite(v) ? v.ToString("0.####", Invariant) : "";
    }

    private static void Models(StringBuilder md, TrainerOptions options)
    {
        md.AppendLine("## Models");
        md.AppendLine();
        if (!File.Exists(options.LatestModelFile))
        {
            md.AppendLine("No trained model yet; run `train`.");
            return;
        }

        var version = File.ReadAllText(options.LatestModelFile).Trim();
        var directory = Path.Combine(options.ModelDirectory, version);
        var aim = AimModelStore.Load(directory).Manifest;
        var outcomes = OutcomeModelStore.Load(Path.Combine(directory, OutcomeModelStore.SubfolderName)).Manifest;

        md.AppendLine($"Latest model `{version}`. Outcome scores are leave-one-demo-out: each demo is predicted by a model trained on the others.");
        md.AppendLine();
        md.AppendLine("### Outcome models");
        md.AppendLine();
        md.AppendLine("| Target | Rows | Base rate | AUC (mean ± sd) | Log-loss | Brier | Top features (AUC drop when shuffled) |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|---|");
        foreach (var t in outcomes.Targets)
        {
            var cv = t.CrossValidation;
            var top = string.Join(", ", t.Importance.Take(4).Select(i => $"{i.Feature} {i.AucDrop:+0.000;-0.000}"));
            md.AppendLine($"| {t.Target} | {t.TrainingRows} | {Rate(t.PositiveRows, t.TrainingRows)} | {D(cv.MeanAuc)} ± {D(cv.SdAuc)} | " +
                          $"{D(cv.MeanLogLoss)} | {D(cv.MeanBrier)} | {top} |");
        }

        md.AppendLine();
        md.AppendLine("### Aim category models (rule labels)");
        md.AppendLine();
        md.AppendLine("These learn the rules, so a high AUC only shows the model reproduces them. AUC is on held-out demos:");
        md.AppendLine("leave-one-demo-out below 10 demos, otherwise a 20% hold-out of the demos.");
        md.AppendLine();
        md.AppendLine("| Category | Rows | Positives | AUC |");
        md.AppendLine("|---|---:|---:|---:|");
        foreach (var c in aim.Categories)
            md.AppendLine($"| {c.Category} | {c.TrainingRows} | {c.PositiveRows} | {D(c.Auc)} |");
        md.AppendLine();

        static string D(double? v) => v is { } x ? x.ToString("0.000", Invariant) : "n/a";
    }

    private static double? RateOf(List<FeatureRow> rows, Func<FeatureRow, bool?> label)
    {
        var labeled = rows.Select(label).OfType<bool>().ToList();
        return labeled.Count > 0 ? labeled.Count(l => l) / (double)labeled.Count : null;
    }

    private static string WithInterval(FeatureBin bin)
    {
        if (bin.Count == 0)
            return "–";

        var (low, high) = bin.Interval;
        return $"{bin.Rate:P0} ({low:P0}–{high:P0})";
    }

    private static string Rate(int positives, int count) => count > 0 ? (positives / (double)count).ToString("P0", Invariant) : "–";

    private static string P(double? rate) => rate is { } r ? r.ToString("P0", Invariant) : "–";

    private static double Auc(List<FeatureRow> rows, Func<FeatureRow, float> score) =>
        PairAuc(rows.Select(r => (score(r), r.Labels.FirstShotHit == true)).ToList());

    /// <summary>Mann–Whitney AUC: the chance that a random positive scores higher than a random negative.</summary>
    private static double PairAuc(List<(float Score, bool Label)> rows)
    {
        var positives = rows.Count(r => r.Label);
        var negatives = rows.Count - positives;
        if (positives == 0 || negatives == 0)
            return double.NaN;

        var sorted = rows.OrderBy(r => r.Score).ToList();
        double rankSum = 0;
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j < sorted.Count && sorted[j].Score == sorted[i].Score)
                j++;

            var averageRank = (i + j + 1) / 2.0; // ranks are 1-based
            rankSum += averageRank * sorted.Skip(i).Take(j - i).Count(r => r.Label);
            i = j;
        }

        return (rankSum - positives * (positives + 1) / 2.0) / ((double)positives * negatives);
    }
}
