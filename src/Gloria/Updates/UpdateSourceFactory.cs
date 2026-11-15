using Velopack.Sources;

namespace Gloria.Updates;

/// <summary>The single place that decides where updates come from.</summary>
public static class UpdateSourceFactory
{
    /// <summary>
    /// The public repo whose GitHub Releases hold the <c>vpk pack</c> output, published by
    /// <c>.github/workflows/release.yml</c>. No token is shipped, so the repo must stay public.
    /// </summary>
    public const string ReleasesRepository = "https://github.com/xonium/Gloria";

    /// <summary>
    /// A local folder holding <c>vpk pack</c> output (e.g. the repo's <c>releases/</c>). When set, it
    /// replaces GitHub, so in-app updates can be tested without publishing a release.
    /// </summary>
    public const string LocalReleasesVariable = "GLORIA_UPDATE_DIR";

    public static IUpdateSource Create()
    {
        if (Environment.GetEnvironmentVariable(LocalReleasesVariable) is { Length: > 0 } dir)
        {
            return new SimpleFileSource(new DirectoryInfo(dir));
        }

        return new GithubSource(ReleasesRepository, accessToken: null, prerelease: false);
    }
}
