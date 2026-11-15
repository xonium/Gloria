using Gloria.ViewModels;
using Microsoft.Extensions.Logging;

namespace Gloria.Updates;

public enum UpdateState
{
	/// <summary>Not checked yet, or a development build that can't update.</summary>
	Idle,
	Checking,
	UpToDate,
	Downloading,

	/// <summary>Downloaded; applied by <see cref="UpdateNotifier.Restart"/>.</summary>
	Ready,
	Failed,
}

/// <summary>
/// Checks for updates in the background and keeps one shared state for the sidebar and the Welcome screen.
/// An update that is found is downloaded right away (deltas are small) and applied when the user restarts.
/// Members are meant to be used from the UI thread.
/// </summary>
public sealed class UpdateNotifier(UpdateService updates, ILogger<UpdateNotifier> log) : ObservableObject
{
	private UpdateState _state;
	private string? _version;
	private int _progress;
	private string _error = "";

	public UpdateState State
	{
		get => _state;
		private set
		{
			if (Set(ref _state, value))
				Notify();
		}
	}

	public bool IsInstalled => updates.IsInstalled;

	/// <summary>The installed version, or null for a development build.</summary>
	public string? CurrentVersion => updates.CurrentVersion;

	/// <summary>The update's version once one is found.</summary>
	public string? AvailableVersion => _version;

	public bool IsReady => State == UpdateState.Ready;

	public bool IsNotReady => !IsReady;

	public bool CanCheck => IsInstalled && State is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Failed;

	/// <summary>One line for the sidebar.</summary>
	public string StatusText => State switch
	{
		_ when !IsInstalled => "Development build",
		UpdateState.Checking => $"Gloria {CurrentVersion} · checking for updates…",
		UpdateState.UpToDate => $"Gloria {CurrentVersion} · up to date",
		UpdateState.Downloading => $"Downloading {_version}: {_progress}%",
		UpdateState.Ready => $"Version {_version} is ready",
		UpdateState.Failed => $"Gloria {CurrentVersion} · couldn't check for updates",
		_ => $"Gloria {CurrentVersion}",
	};

	public string Error => _error;

	/// <summary>Checks, and downloads an update if there is one. Failures are logged and shown, never thrown.</summary>
	public async Task CheckAsync()
	{
		if (!CanCheck)
			return;

		State = UpdateState.Checking;
		try
		{
			_version = await updates.CheckForUpdateAsync();
			if (_version is null)
			{
				State = UpdateState.UpToDate;
				return;
			}

			_progress = 0;
			State = UpdateState.Downloading;
			await updates.DownloadUpdateAsync(p => MainThread.BeginInvokeOnMainThread(() =>
			{
				_progress = p;
				Notify();
			}));
			State = UpdateState.Ready;
		}
		catch (Exception ex)
		{
			log.LogWarning(ex, "Update check failed");
			_error = ex.Message;
			State = UpdateState.Failed;
		}
	}

	/// <summary>Applies the downloaded update and restarts the app. Does nothing unless an update is ready.</summary>
	public void Restart()
	{
		if (IsReady)
			updates.ApplyUpdateAndRestart();
	}

	private void Notify()
	{
		OnPropertyChanged(nameof(StatusText));
		OnPropertyChanged(nameof(IsReady));
		OnPropertyChanged(nameof(IsNotReady));
		OnPropertyChanged(nameof(CanCheck));
		OnPropertyChanged(nameof(AvailableVersion));
		OnPropertyChanged(nameof(Error));
	}
}
