namespace Gloria.Core.Tests.Fixtures;

/// <summary>
/// Locates the real demos in the repo's <c>_demos/</c> folder. They are not in version control,
/// so tests that need them must skip when they are missing.
/// </summary>
public static class DemoFiles
{
    /// <summary>Trait value for tests that read real demos; exclude with <c>--filter "Category!=Demo"</c>.</summary>
    public const string Category = "Demo";

    public static IReadOnlyList<string> All { get; } = Find();

    /// <summary>Returns the first demo, or skips the current test if none is available.</summary>
    public static string FirstOrSkip()
    {
        Assert.SkipWhen(All.Count == 0, "No .dem files found in _demos/.");
        return All[0];
    }

    private static string[] Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gloria.slnx")))
            {
                var demos = Path.Combine(dir.FullName, "_demos");
                return Directory.Exists(demos)
                    ? Directory.GetFiles(demos, "*.dem").Order(StringComparer.Ordinal).ToArray()
                    : [];
            }
        }

        return [];
    }
}
