using Gloria.Data;
using Gloria.Services;
using Gloria.Updates;

namespace Gloria.Pages;

/// <summary>Shown when nothing is imported and no player is chosen yet.</summary>
public partial class WelcomePage : ContentPage
{
	private readonly ImportQueue _queue;
	private readonly AppSettings _settings;
	private readonly UpdateNotifier _updates;

	public WelcomePage(ImportQueue queue, AppSettings settings, UpdateNotifier updates)
	{
		_queue = queue;
		_settings = settings;
		_updates = updates;
		InitializeComponent();
		ShowUpdateState();
		// Same state as the sidebar, which checks at startup.
		_updates.PropertyChanged += (_, _) => ShowUpdateState();
	}

	private void ShowUpdateState()
	{
		VersionLabel.Text = _updates.StatusText;
		UpdateButton.Text = _updates.IsReady ? "Restart to update" : "Check for updates";
		UpdateButton.IsEnabled = _updates.CanCheck || _updates.IsReady;
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

	private async void OnDemosChosen(object? sender, IReadOnlyList<string> files) =>
		await ImportFilesAsync(files);

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

		await Shell.Current.GoToAsync("//player");
	}

	private void ShowStatus(string text)
	{
		StatusLabel.Text = text;
		StatusLabel.IsVisible = true;
	}

	private async void OnCheckUpdates(object? sender, EventArgs e)
	{
		if (!_updates.IsReady)
			await _updates.CheckAsync();

		if (_updates.IsReady && await DisplayAlertAsync("Update ready", $"Version {_updates.AvailableVersion} is ready. Restart now?", "Restart", "Later"))
			_updates.Restart();
	}
}
