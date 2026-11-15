using Velopack;

namespace Gloria.Updates;

/// <summary>Checks for, downloads and applies Velopack updates. Does nothing when not installed (e.g. run from bin/).</summary>
public sealed class UpdateService
{
    private readonly UpdateManager _manager = new(UpdateSourceFactory.Create());
    private UpdateInfo? _available;

    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>The installed version, or null for a development build.</summary>
    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <returns>The version of the available update, or null if there is none.</returns>
    public async Task<string?> CheckForUpdateAsync()
    {
        if (!IsInstalled)
        {
            return null;
        }

        _available = await _manager.CheckForUpdatesAsync();
        return _available?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadUpdateAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_available is null)
        {
            throw new InvalidOperationException("No update available. Call CheckForUpdateAsync first.");
        }

        return _manager.DownloadUpdatesAsync(_available, progress, cancellationToken);
    }

    public void ApplyUpdateAndRestart()
    {
        if (_available is null)
        {
            throw new InvalidOperationException("No update available. Call CheckForUpdateAsync first.");
        }

        _manager.ApplyUpdatesAndRestart(_available.TargetFullRelease);
    }
}
