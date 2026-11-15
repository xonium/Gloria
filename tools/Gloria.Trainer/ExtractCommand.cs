using System.Diagnostics;
using Gloria.Core.Analysis;
using Gloria.Core.Ingestion;

namespace Gloria.Trainer;

/// <summary>Parses each demo once per analysis version and caches its encounters as CSV.</summary>
public static class ExtractCommand
{
    public static async Task RunAsync(TrainerOptions options)
    {
        var demos = Directory.Exists(options.DemoDirectory)
            ? Directory.GetFiles(options.DemoDirectory, "*.dem").Order(StringComparer.Ordinal).ToList()
            : [];
        if (demos.Count == 0)
            throw new TrainerException($"No .dem files in {options.DemoDirectory}.");

        Console.WriteLine($"extract: {demos.Count} demos, analysis version {TrainerOptions.AnalysisVersion}");
        var total = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            demos,
            new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism },
            async (path, ct) => await ExtractAsync(options, path, ct));
        Console.WriteLine($"extract: done in {total.Elapsed.TotalSeconds:F0} s");
    }

    private static async Task ExtractAsync(TrainerOptions options, string path, CancellationToken ct)
    {
        var name = Path.GetFileName(path);
        string hash;
        await using (var stream = File.OpenRead(path))
            hash = await DemoHasher.ComputeSha256Async(stream, ct);

        var csv = FeatureCsv.CsvPath(options, hash);
        if (File.Exists(csv) && !options.Force)
        {
            Console.WriteLine($"  {name}: cached");
            return;
        }

        var watch = Stopwatch.StartNew();
        ParsedDemo demo;
        await using (var stream = File.OpenRead(path))
            demo = await new DemoFileDemoParser().ParseAsync(stream, cancellationToken: ct);
        var parseSeconds = watch.Elapsed.TotalSeconds;

        watch.Restart();
        var analysis = new DemoAnalyzer().Analyze(demo);
        var analyzeSeconds = watch.Elapsed.TotalSeconds;

        var summary = new DemoSummary(
            hash,
            name,
            demo.MapName,
            demo.Rounds.Count,
            demo.Timeline.SteamIds.Count,
            analysis.Encounters.Count,
            analysis.Segmentation.DroppedWithoutShots,
            analysis.Segmentation.DroppedDeathsWithoutShots,
            analysis.Segmentation.DroppedUnseen,
            parseSeconds,
            analyzeSeconds,
            SpottedRunLengths(demo));
        FeatureCsv.Write(
            csv,
            analysis.Encounters.Select(a => FeatureCsv.ToRow(hash, demo.MapName, a)),
            summary,
            FeatureCsv.SummaryPath(options, hash));

        Console.WriteLine($"  {name}: {demo.MapName}, {demo.Rounds.Count} rounds, {analysis.Encounters.Count} encounters " +
                          $"(parse {parseSeconds:F1} s, analyze {analyzeSeconds:F1} s)");
    }

    /// <summary>How many ticks the spotted state stays set at a time: the 12 most common run lengths.</summary>
    private static Dictionary<int, int> SpottedRunLengths(ParsedDemo demo)
    {
        var timeline = demo.Timeline;
        var counts = new Dictionary<int, int>();
        foreach (var observer in timeline.SteamIds)
        {
            foreach (var target in timeline.SteamIds.Where(t => t != observer))
            {
                var run = 0;
                for (var tick = timeline.FirstTick; tick <= timeline.LastTick + 1; tick++)
                {
                    if (tick <= timeline.LastTick && timeline.IsSpottedBy(target, observer, tick))
                    {
                        run++;
                    }
                    else if (run > 0)
                    {
                        counts[run] = counts.GetValueOrDefault(run) + 1;
                        run = 0;
                    }
                }
            }
        }

        return counts.OrderByDescending(kv => kv.Value).Take(12).ToDictionary();
    }
}
