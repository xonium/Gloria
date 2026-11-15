using Gloria.Ml;

namespace Gloria.Trainer;

/// <summary>Trains the aim bundle (rule labels) and the outcome bundle (labels from the demos) on the cached rows.</summary>
public static class TrainCommand
{
    public static void Run(TrainerOptions options)
    {
        var (rows, _) = FeatureCsv.ReadAll(options);
        var demoCount = rows.Select(r => r.DemoHash).Distinct().Count();
        Console.WriteLine($"train: {rows.Count} encounters from {demoCount} demos");

        var trainedAt = DateTimeOffset.UtcNow;
        var aim = new AimModelTrainer().Train(
            rows.Select(r => new TrainingEncounter(r.Features, r.DemoHash)).ToList(),
            trainedAt);
        var outcomes = new OutcomeModelTrainer().Train(
            rows.Select(r => new OutcomeTrainingEncounter(r.Features, r.DemoHash, r.Labels)).ToList(),
            trainedAt);

        var directory = Path.Combine(options.ModelDirectory, aim.Manifest.ModelVersion);
        AimModelStore.Save(aim, directory);
        OutcomeModelStore.Save(outcomes, Path.Combine(directory, OutcomeModelStore.SubfolderName));
        File.WriteAllText(options.LatestModelFile, aim.Manifest.ModelVersion);

        foreach (var c in aim.Manifest.Categories)
            Console.WriteLine($"  aim {c.Category,-13} rows {c.TrainingRows,5}  positives {c.PositiveRows,5}  LODO AUC {Format(c.Auc)}");
        foreach (var t in outcomes.Manifest.Targets)
        {
            var cv = t.CrossValidation;
            var top = string.Join(", ", t.Importance.Take(3).Select(i => $"{i.Feature} {i.AucDrop:+0.000;-0.000}"));
            Console.WriteLine($"  outcome {t.Target,-12} rows {t.TrainingRows,5}  positives {t.PositiveRows,5}  " +
                              $"LODO AUC {Format(cv.MeanAuc)} ± {Format(cv.SdAuc)}  top: {top}");
        }

        Console.WriteLine($"train: saved {directory}");
    }

    private static string Format(double? value) => value is { } v ? v.ToString("0.000") : "n/a";
}
