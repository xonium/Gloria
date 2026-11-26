using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Gloria.Data;
using Gloria.Data.Import;
using Gloria.Data.Queries;
using Gloria.Services;
using Gloria.Updates;

namespace Gloria.ViewModels;

public sealed class NavItem(string title, string route, ICommand open) : ObservableObject
{
	private bool _active;

	public string Title { get; } = title;

	public string Route { get; } = route;

	public ICommand Open { get; } = open;

	public bool Active
	{
		get => _active;
		set
		{
			if (Set(ref _active, value))
			{
				OnPropertyChanged(nameof(Background));
				OnPropertyChanged(nameof(Foreground));
				OnPropertyChanged(nameof(Font));
			}
		}
	}

	public Color Background => Active ? Display.AccentSoft : Colors.Transparent;

	public Color Foreground => Active ? Color.FromArgb("#e3eefc") : Display.Text2;

	public string Font => Active ? "OpenSansSemibold" : "OpenSansRegular";
}

public sealed class ShellViewModel : ObservableObject
{
	private readonly ProfileState _profiles;
	private readonly ImportQueue _queue;
	private readonly GloriaReadModel _readModel;
	private readonly AppSettings _settings;
	private readonly UserModelTraining _training;
	private int _selectedProfileIndex = -1;

	public ShellViewModel(
		ProfileState profiles,
		ImportQueue queue,
		GloriaReadModel readModel,
		AppSettings settings,
		DemoImporter importer,
		UserModelTraining training,
		UpdateNotifier updates)
	{
		Updates = updates;
		CheckUpdatesCommand = new Command(async () => await Updates.CheckAsync());
		RestartToUpdateCommand = new Command(async () => await RestartToUpdateAsync());
		_profiles = profiles;
		_settings = settings;
		_training = training;
		_queue = queue;
		_readModel = readModel;
		AnalysisLabel = $"Analysis {importer.AnalysisVersionKey}";
		_training.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(UserModelTraining.ClassifierLabel))
				OnPropertyChanged(nameof(ClassifierLabel));
		};
		NavItems =
		[
			new NavItem("Overview", "overview", Go("//overview")),
			new NavItem("Matches", "matches", Go("//matches")),
			new NavItem("Progress", "trends", Go("//trends")),
			new NavItem("Compare", "compare", Go("//compare")),
			new NavItem("Spray", "spray", Go("//spray")),
			new NavItem("Import", "import", Go("//import")),
			new NavItem("Player", "player", Go("//player")),
		];
		TrackAnotherCommand = Go("//player");
		OpenQueueCommand = Go("//import");

		_profiles.Profiles.CollectionChanged += (_, _) => RefreshProfiles();
		_profiles.CurrentChanged += (_, _) => SyncSelection();
		_queue.Changed += (_, _) => RefreshImport();
	}

	public ObservableCollection<string> ProfileNames { get; } = [];

	public IReadOnlyList<NavItem> NavItems { get; }

	public ICommand TrackAnotherCommand { get; }

	public ICommand OpenQueueCommand { get; }

	/// <summary>Update state for the sidebar footer.</summary>
	public UpdateNotifier Updates { get; }

	public ICommand CheckUpdatesCommand { get; }

	public ICommand RestartToUpdateCommand { get; }

	public string AnalysisLabel { get; }

	public string ClassifierLabel => _training.ClassifierLabel;

	public int SelectedProfileIndex
	{
		get => _selectedProfileIndex;
		set
		{
			if (!Set(ref _selectedProfileIndex, value) || value < 0)
				return;

			// The Picker's dropdown is still closing here. Switching fans out into every page's refresh,
			// so let the dropdown finish first, or it stays stuck open.
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (value == _selectedProfileIndex && value < _profiles.Profiles.Count)
					_profiles.Current = _profiles.Profiles[value];
			});
		}
	}

	public string ProfileDetail => _profiles.Current is { } p ? p.SteamId.ToString() : "No player chosen yet";

	public bool IsImporting => _queue.Remaining > 0;

	public string ImportText => _queue.Remaining == 1 ? "Importing 1 demo…" : $"Importing {_queue.Remaining} demos…";

	public double ImportProgress => _queue.Items.FirstOrDefault(i => i.IsRunning)?.Progress ?? 0;

	/// <summary>Loads profiles, rescans the saved folders and opens the right first screen.</summary>
	public async Task StartAsync()
	{
		LegacyPreferences.MoveInto(_settings);
		await _profiles.LoadAsync();
		RefreshProfiles();
		foreach (var folder in _settings.DemoFolders)
			await _queue.EnqueueFolderAsync(folder);

		if (_profiles.Current is not null)
			await Shell.Current.GoToAsync("//overview");
		else if (await _readModel.DemoCountAsync() > 0 || _queue.Remaining > 0)
			await Shell.Current.GoToAsync("//player");
		else
			await Shell.Current.GoToAsync("//welcome");

		// Runs alongside the catch-up below; it never throws.
		_ = Updates.CheckAsync();

		// Encounters classified by another model (an update shipped a new base model, or a retrain was interrupted).
		await _training.CatchUpAsync();
	}

	public void OnNavigated(string location)
	{
		foreach (var item in NavItems)
			item.Active = location.Contains($"//{item.Route}", StringComparison.Ordinal);
	}

	private async Task RestartToUpdateAsync()
	{
		// An interrupted import loses only the demo in progress; the folder rescan at startup picks it up again.
		if (_queue.Remaining > 0 && Shell.Current?.CurrentPage is { } page
			&& !await page.DisplayAlertAsync("Import running", $"{ImportText} Restart anyway? The rest is imported after the update.", "Restart", "Wait"))
		{
			return;
		}

		Updates.Restart();
	}

	private void RefreshProfiles()
	{
		// Rebuilding the Picker's items resets its selection, so only do it when the names changed.
		if (!ProfileNames.SequenceEqual(_profiles.Profiles.Select(p => p.Name)))
		{
			ProfileNames.Clear();
			foreach (var p in _profiles.Profiles)
				ProfileNames.Add(p.Name);
			_selectedProfileIndex = -1;
		}

		SyncSelection();
	}

	private void SyncSelection()
	{
		var index = _profiles.Current is null ? -1 : _profiles.Profiles.IndexOf(_profiles.Current);
		// A switch made in the Picker already shows; writing it back would touch the control again.
		if (index != _selectedProfileIndex)
		{
			_selectedProfileIndex = index;
			OnPropertyChanged(nameof(SelectedProfileIndex));
		}

		OnPropertyChanged(nameof(ProfileDetail));
	}

	private void RefreshImport()
	{
		OnPropertyChanged(nameof(IsImporting));
		OnPropertyChanged(nameof(ImportText));
		OnPropertyChanged(nameof(ImportProgress));
	}

	private static Command Go(string route) => new(async () => await Shell.Current.GoToAsync(route));
}
