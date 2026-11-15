using Gloria.Data;
using Gloria.Updates;

namespace Gloria;

public partial class MainPage : ContentPage
{
	private readonly UpdateService _updates;

	public MainPage(UpdateService updates)
	{
		_updates = updates;
		InitializeComponent();

		VersionLabel.Text = $"Version: {_updates.CurrentVersion ?? "development build"}";
		DatabaseLabel.Text = $"Database: {DatabaseLocation.DefaultPath}";
		CheckUpdatesButton.IsEnabled = _updates.IsInstalled;
		UpdateStatusLabel.Text = _updates.IsInstalled ? "" : "Updates are only available in an installed copy.";
	}

	private async void OnCheckUpdatesClicked(object? sender, EventArgs e)
	{
		CheckUpdatesButton.IsEnabled = false;
		try
		{
			UpdateStatusLabel.Text = "Checking for updates...";
			var version = await _updates.CheckForUpdateAsync();
			if (version is null)
			{
				UpdateStatusLabel.Text = "You are up to date.";
				return;
			}

			await _updates.DownloadUpdateAsync(p => MainThread.BeginInvokeOnMainThread(
				() => UpdateStatusLabel.Text = $"Downloading {version}: {p}%"));

			if (await DisplayAlertAsync("Update ready", $"Version {version} is ready. Restart now?", "Restart", "Later"))
			{
				_updates.ApplyUpdateAndRestart();
			}
			else
			{
				UpdateStatusLabel.Text = $"Version {version} will be installed on next restart.";
			}
		}
		catch (Exception ex)
		{
			UpdateStatusLabel.Text = $"Update failed: {ex.Message}";
		}
		finally
		{
			CheckUpdatesButton.IsEnabled = true;
		}
	}
}
