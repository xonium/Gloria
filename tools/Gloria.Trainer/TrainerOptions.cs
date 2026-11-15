using Gloria.Core.Analysis;
using Gloria.Core.Ingestion;

namespace Gloria.Trainer;

public sealed class TrainerException(string message) : Exception(message);

public sealed record TrainerOptions(
    IReadOnlyList<string> Commands,
    string RepoRoot,
    string DemoDirectory,
    string OutputDirectory,
    int Parallelism,
    bool Force)
{
    private static readonly string[] Known = ["extract", "train", "report", "publish"];

    public string FeatureDirectory => Path.Combine(OutputDirectory, "features");

    public string ModelDirectory => Path.Combine(OutputDirectory, "models");

    /// <summary>Text file naming the most recently trained model version folder.</summary>
    public string LatestModelFile => Path.Combine(ModelDirectory, "latest.txt");

    public string BaseModelDirectory => Path.Combine(RepoRoot, "src", "Gloria", "models", "base");

    /// <summary>Cache key of the analysis code; a demo is re-extracted when any stage's version changes.</summary>
    public static string AnalysisVersion { get; } = new DemoAnalyzer().VersionKeyFor(new DemoFileDemoParser());

    public static TrainerOptions? Parse(string[] args)
    {
        if (args.Length == 0)
            return null;

        var commands = args[0] switch
        {
            "all" => ["extract", "train", "report"],
            var c when Known.Contains(c) => new[] { c },
            _ => null,
        };
        if (commands is null)
            return null;

        var root = FindRepoRoot();
        string demos = Path.Combine(root, "_demos"), output = Path.Combine(root, "artifacts", "training");
        int parallel = 2;
        var force = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--demos": demos = Path.GetFullPath(args[++i]); break;
                case "--out": output = Path.GetFullPath(args[++i]); break;
                case "--parallel": parallel = int.Parse(args[++i]); break;
                case "--force": force = true; break;
                default: return null;
            }
        }

        return new TrainerOptions(commands, root, demos, output, Math.Max(1, parallel), force);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gloria.slnx")))
                return dir.FullName;
        }

        throw new TrainerException("Run the trainer from inside the Gloria repository (Gloria.slnx not found).");
    }
}
