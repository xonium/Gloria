using Gloria.Data;
using Gloria.Services;
using Gloria.Updates;

namespace Gloria.Pages;

/// <summary>Shown when nothing is imported and no player is chosen yet.</summary>
public partial class WelcomePage : ContentPage
{
	private readonly ImportQueue _queue;
	private readonly AppSettings _settings;
	private readonly UpdateService _updates;

	public WelcomePage(ImportQueue queue, AppSettings settings, UpdateService updates)
	{
		_queue = queue;
		_settings = settings;
		_updates = updates;
		InitializeComponent();
		VersionLabel.Text = $"Gloria {_updates.CurrentVersion ?? "development build"}";
		UpdateButton.IsEnabled = _updates.IsInstalled;
	}

	private async void OnPickFolder(object? sender, EventArgs e)
	{
		if (await DemoSources.PickFolderAsync() is { } folder)
			await ImportFoldersAsync([folder]);
	}

	private async void OnUseReplays(object? sender, EventArgs e)
	{
		var folders = DemoSources.FindCs2ReplayFolders();
		if (folders.Count == 0)
		{
			ShowStatus("Couldn't find a CS2 replays folder on this PC. Choose the folder yourself.");
			return;
		}

		await ImportFoldersAsync(folders);
	}

	private async void OnPickFiles(object? sender, TappedEventArgs e) =>
		await ImportFilesAsync(await DemoSources.PickFilesAsync());

	private void OnDragOver(object? sender, DragEventArgs e) => DemoSources.AcceptDrag(e);

	private async void OnDrop(object? sender, DropEventArgs e) =>
		await ImportFilesAsync(await DemoSources.DroppedDemosAsync(e));

	private async Task ImportFoldersAsync(IReadOnlyList<string> folders)
	{
		foreach (var folder in folders)
		{
			_settings.AddDemoFolder(folder);
			await _queue.EnqueueFolderAsync(folder);
		}

		await ContinueAsync("No .dem files in that folder. Gloria will pick up demos that land there later.");
	}

	private async Task ImportFilesAsync(IReadOnlyList<string> files)
	{
		if (files.Count == 0)
			return;

		await _queue.EnqueueAsync(files);
		await ContinueAsync("Those demos are already imported.");
	}

	private async Task ContinueAsync(string nothingQueued)
	{
		if (_queue.Items.Count == 0)
		{
			ShowStatus(nothingQueued);
			return;
		}

		await Shell.Current.GoToAsync("//setup");
	}

	private void ShowStatus(string text)
	{
		StatusLabel.Text = text;
		StatusLabel.IsVisible = true;
	}

	private async void OnCheckUpdates(object? sender, EventArgs e)
	{
		UpdateButton.IsEnabled = false;
		try
		{
			var version = await _updates.CheckForUpdateAsync();
			if (version is null)
			{
				VersionLabel.Text = $"Gloria {_updates.CurrentVersion} · up to date";
				return;
			}

			await _updates.DownloadUpdateAsync(p => MainThread.BeginInvokeOnMainThread(() => VersionLabel.Text = $"Downloading {version}: {p}%"));
			if (await DisplayAlertAsync("Update ready", $"Version {version} is ready. Restart now?", "Restart", "Later"))
				_updates.ApplyUpdateAndRestart();
		}
		catch (Exception ex)
		{
			VersionLabel.Text = $"Update failed: {ex.Message}";
		}
		finally
		{
			UpdateButton.IsEnabled = _updates.IsInstalled;
		}
	}
}
