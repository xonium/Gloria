namespace Gloria.Ml;

/// <summary>
/// The user's retrained models, one subfolder per version, plus a <c>current</c> file naming the one in use.
/// A new model is written to its own folder before <c>current</c> is switched to it, so a failed or interrupted
/// retrain never leaves a broken model behind.
/// </summary>
public static class UserModelFolder
{
    public const string CurrentFileName = "current";

    /// <summary>Versions kept besides the current one, to look back at.</summary>
    private const int KeepPrevious = 1;

    /// <summary>
    /// The folder of the model in use: the one <c>current</c> names, or <paramref name="root"/> itself if it holds
    /// a model directly (the layout before versions). Null if there is no user model.
    /// </summary>
    public static string? ActiveDirectory(string root)
    {
        var pointer = Path.Combine(root, CurrentFileName);
        if (File.Exists(pointer))
        {
            var name = File.ReadAllText(pointer).Trim();
            var directory = Path.Combine(root, name);
            if (name.Length > 0 && name == Path.GetFileName(name) && AimModelStore.Exists(directory))
                return directory;
        }

        return AimModelStore.Exists(root) ? root : null;
    }

    /// <summary>Saves the bundle as a new version, makes it current and removes older versions. Returns its folder.</summary>
    public static string Promote(string root, AimModelBundle bundle)
    {
        Directory.CreateDirectory(root);
        var name = bundle.Manifest.ModelVersion;
        for (var i = 2; Directory.Exists(Path.Combine(root, name)); i++)
            name = $"{bundle.Manifest.ModelVersion}-{i}";

        var directory = Path.Combine(root, name);
        AimModelStore.Save(bundle, directory);

        var pointer = Path.Combine(root, CurrentFileName);
        var temporary = pointer + ".tmp";
        File.WriteAllText(temporary, name);
        File.Move(temporary, pointer, overwrite: true);

        Prune(root, name);
        return directory;
    }

    private static void Prune(string root, string current)
    {
        var older = new DirectoryInfo(root).GetDirectories()
            .Where(d => d.Name != current && AimModelStore.Exists(d.FullName))
            .OrderByDescending(d => d.CreationTimeUtc)
            .Skip(KeepPrevious);
        foreach (var directory in older)
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Still open somewhere; it goes next time.
            }
        }
    }
}
