using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Gloria.Core.Classification;
using Gloria.Data.Import;
using Gloria.Data.Queries;
using Gloria.Services;

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
	public const string FoldersKey = "demo-folders";

	private readonly ProfileState _profiles;
	private readonly ImportQueue _queue;
	private readonly GloriaReadModel _readModel;
	private int _selectedProfileIndex = -1;

	public ShellViewModel(ProfileState profiles, ImportQueue queue, GloriaReadModel readModel, DemoImporter importer, IAimClassifier classifier)
	{
		_profiles = profiles;
		_queue = queue;
		_readModel = readModel;
		AnalysisLabel = $"Analysis {importer.AnalysisVersionKey}";
		ClassifierLabel = $"Classifier: {classifier.Source}";
		NavItems =
		[
			new NavItem("Overview", "overview", Go("//overview")),
			new NavItem("Matches", "matches", Go("//matches")),
			new NavItem("Progress", "trends", Go("//trends")),
			new NavItem("Folders & player", "setup", Go("//setup")),
		];
		TrackAnotherCommand = Go("//setup");
		OpenQueueCommand = Go("//matches");

		_profiles.Profiles.CollectionChanged += (_, _) => RefreshProfiles();
		_profiles.CurrentChanged += (_, _) => RefreshProfiles();
		_queue.Changed += (_, _) => RefreshImport();
	}

	public ObservableCollection<string> ProfileNames { get; } = [];

	public IReadOnlyList<NavItem> NavItems { get; }

	public ICommand TrackAnotherCommand { get; }

	public ICommand OpenQueueCommand { get; }

	public string AnalysisLabel { get; }

	public string ClassifierLabel { get; }

	public int SelectedProfileIndex
	{
		get => _selectedProfileIndex;
		set
		{
			if (!Set(ref _selectedProfileIndex, value))
				return;
			if (value >= 0 && value < _profiles.Profiles.Count)
				_profiles.Current = _profiles.Profiles[value];
			OnPropertyChanged(nameof(ProfileDetail));
		}
	}

	public string ProfileDetail => _profiles.Current is { } p ? p.SteamId.ToString() : "No player chosen yet";

	public bool IsImporting => _queue.Remaining > 0;

	public string ImportText => _queue.Remaining == 1 ? "Importing 1 demo…" : $"Importing {_queue.Remaining} demos…";

	public double ImportProgress => _queue.Items.FirstOrDefault(i => i.IsRunning)?.Progress ?? 0;

	/// <summary>Loads profiles, rescans the saved folders and opens the right first screen.</summary>
	public async Task StartAsync()
	{
		await _profiles.LoadAsync();
		RefreshProfiles();
		foreach (var folder in SavedFolders())
			await _queue.EnqueueFolderAsync(folder);

		if (_profiles.Current is not null)
			await Shell.Current.GoToAsync("//overview");
		else if (await _readModel.DemoCountAsync() > 0 || _queue.Remaining > 0)
			await Shell.Current.GoToAsync("//setup");
		else
			await Shell.Current.GoToAsync("//welcome");
	}

	public void OnNavigated(string location)
	{
		foreach (var item in NavItems)
			item.Active = location.Contains($"//{item.Route}", StringComparison.Ordinal);
	}

	public static IReadOnlyList<string> SavedFolders() =>
		Preferences.Default.Get(FoldersKey, "").Split('|', StringSplitOptions.RemoveEmptyEntries);

	public static void SaveFolder(string folder)
	{
		var folders = SavedFolders().Append(folder).Distinct(StringComparer.OrdinalIgnoreCase);
		Preferences.Default.Set(FoldersKey, string.Join('|', folders));
	}

	public static void RemoveFolder(string folder)
	{
		var folders = SavedFolders().Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
		Preferences.Default.Set(FoldersKey, string.Join('|', folders));
	}

	private void RefreshProfiles()
	{
		ProfileNames.Clear();
		foreach (var p in _profiles.Profiles)
			ProfileNames.Add(p.Name);
		_selectedProfileIndex = _profiles.Current is null ? -1 : _profiles.Profiles.IndexOf(_profiles.Current);
		OnPropertyChanged(nameof(SelectedProfileIndex));
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
