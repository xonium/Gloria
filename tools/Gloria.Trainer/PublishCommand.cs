using Gloria.Ml;

namespace Gloria.Trainer;

/// <summary>Replaces the base model shipped with the app by the latest trained bundle.</summary>
public static class PublishCommand
{
    public static void Run(TrainerOptions options)
    {
        if (!File.Exists(options.LatestModelFile))
            throw new TrainerException("No trained model yet. Run 'train' first.");

        var version = File.ReadAllText(options.LatestModelFile).Trim();
        var source = Path.Combine(options.ModelDirectory, version);
        if (!AimModelStore.Exists(source))
            throw new TrainerException($"The latest model folder {source} is incomplete.");

        // Validate before touching the shipped copy.
        AimModelStore.Load(source);
        OutcomeModelStore.Load(Path.Combine(source, OutcomeModelStore.SubfolderName));

        var target = options.BaseModelDirectory;
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        Console.WriteLine($"publish: model {version} copied to {Path.GetRelativePath(options.RepoRoot, target)}");
    }
}
