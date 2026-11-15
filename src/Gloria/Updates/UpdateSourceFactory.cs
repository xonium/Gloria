using Velopack.Sources;

namespace Gloria.Updates;

/// <summary>The single place that decides where updates come from.</summary>
public static class UpdateSourceFactory
{
    /// <summary>Overrides <see cref="LocalReleasesDirectory"/> when set.</summary>
    public const string LocalReleasesVariable = "GLORIA_UPDATE_DIR";

    /// <summary>
    /// Phase 1: a local folder holding <c>vpk pack</c> output. Copy the repo's <c>releases/</c>
    /// folder here (or point <see cref="LocalReleasesVariable"/> at it) to test in-app updates.
    /// </summary>
    public static string LocalReleasesDirectory =>
        Environment.GetEnvironmentVariable(LocalReleasesVariable) is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gloria.Releases");

    public static IUpdateSource Create()
    {
        // Phase 2: GitHub Releases on a public repo (no token is shipped in the app), e.g.
        // return new GithubSource("https://github.com/<owner>/<releases-repo>", accessToken: null, prerelease: false);
        return new SimpleFileSource(new DirectoryInfo(LocalReleasesDirectory));
    }
}
