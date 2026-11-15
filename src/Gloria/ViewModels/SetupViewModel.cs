using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Gloria.Data;
using Gloria.Data.Queries;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed class CandidateItem(PlayerCandidate candidate, int demoCount, bool tracked, Action<CandidateItem> select) : ObservableObject
{
	private bool _selected;

	public ulong SteamId => candidate.SteamId;

	public string Name => candidate.Name;

	public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

	public string Detail => candidate.SteamId
		+ (candidate.OtherNames.Count > 0 ? $" · also played as {string.Join(", ", candidate.OtherNames.Take(2))}" : "")
		+ (tracked ? " · already tracked" : "");

	public string SeenText => $"in {candidate.DemoCount} of {demoCount}";

	public double Fraction => demoCount == 0 ? 0 : (double)candidate.DemoCount / demoCount;

	public bool Suggested { get; init; }

	private Command? _selectCommand;

	public Command SelectCommand => _selectCommand ??= new Command(() => select(this));

	public bool IsSelected
	{
		get => _selected;
		set
		{
			if (Set(ref _selected, value))
			{
				OnPropertyChanged(nameof(Background));
				OnPropertyChanged(nameof(Stroke));
			}
		}
	}

	public Color Background => IsSelected ? Display.AccentSofter : Display.Panel2;

	public Color Stroke => IsSelected ? Display.Accent : Display.Line;
}

public sealed class FolderItem(string path, Action<string> remove)
{
	public string Path { get; } = path;

	public Command RemoveCommand { get; } = new(() => remove(path));
}

public sealed partial class SetupViewModel : ObservableObject
{
	private readonly GloriaReadModel _readModel;
	private readonly ImportQueue _queue;
	private readonly ProfileState _profiles;
	private readonly AppSettings _settings;
	private CandidateItem? _selected;
	private string _manualSteamId = "";
	private string _message = "";

	public SetupViewModel(GloriaReadModel readModel, ImportQueue queue, ProfileState profiles, AppSettings settings)
	{
		_readModel = readModel;
		_queue = queue;
		_profiles = profiles;
		_settings = settings;
		AddFolderCommand = new Command(async () => await AddFolderAsync());
		AddFilesCommand = new Command(async () => await AddFilesAsync());
		StartCommand = new Command(async () => await StartAsync());
		_queue.Changed += (_, _) => OnQueueChanged();
		_queue.DemoImported += async (_, _) => await RefreshAsync();
	}

	public ObservableCollection<CandidateItem> Candidates { get; } = [];

	public ObservableCollection<FolderItem> Folders { get; } = [];

	public ICommand AddFolderCommand { get; }

	public ICommand AddFilesCommand { get; }

	public ICommand StartCommand { get; }

	public string Title => _profiles.Current is null ? "Which player is you?" : "Track another player";

	public string Intro => _profiles.Current is null
		? "Players found so far, most frequent first. The one in every demo is usually you."
		: "Each player you track gets a separate profile, built from the demos already imported.";

	public string ManualSteamId
	{
		get => _manualSteamId;
		set
		{
			if (Set(ref _manualSteamId, value))
				OnPropertyChanged(nameof(StartText));
		}
	}

	public string Message { get => _message; private set => Set(ref _message, value); }

	public string StartText => ParseSteamId(ManualSteamId) is { } id
		? $"Start analyzing {id}"
		: _selected is { } c ? $"Start analyzing as {c.Name}" : "Choose a player first";

	public bool IsReading => _queue.Remaining > 0;

	public string ReadingText
	{
		get
		{
			var total = _queue.Items.Count;
			var done = total - _queue.Remaining;
			return $"Reading players from the demos · {done} of {total}";
		}
	}

	public double ReadingProgress => _queue.Items.Count == 0 ? 0 : (double)(_queue.Items.Count - _queue.Remaining) / _queue.Items.Count;

	public bool HasNoCandidates => Candidates.Count == 0;

	public async Task RefreshAsync()
	{
		Folders.Clear();
		foreach (var folder in _settings.DemoFolders)
			Folders.Add(new FolderItem(folder, RemoveFolder));

		var demoCount = await _readModel.DemoCountAsync();
		var candidates = await _readModel.PlayerCandidatesAsync();
		var tracked = _profiles.Profiles.Select(p => p.SteamId).ToHashSet();
		var keep = _selected?.SteamId;
		Candidates.Clear();
		foreach (var (c, i) in candidates.Take(12).Select((c, i) => (c, i)))
		{
			var item = new CandidateItem(c, demoCount, tracked.Contains(c.SteamId), Select)
			{
				Suggested = i == 0 && c.DemoCount == demoCount && demoCount > 1,
			};
			Candidates.Add(item);
		}

		_selected = Candidates.FirstOrDefault(c => c.SteamId == keep)
			?? (_profiles.Current is null ? Candidates.FirstOrDefault(c => c.Suggested) : null);
		if (_selected is not null)
			_selected.IsSelected = true;

		OnPropertyChanged(nameof(HasNoCandidates));
		OnPropertyChanged(nameof(StartText));
		OnPropertyChanged(nameof(Title));
		OnPropertyChanged(nameof(Intro));
		OnQueueChanged();
	}

	private void Select(CandidateItem item)
	{
		if (_selected is not null)
			_selected.IsSelected = false;
		_selected = item;
		item.IsSelected = true;
		OnPropertyChanged(nameof(StartText));
	}

	private async Task StartAsync()
	{
		if (ParseSteamId(ManualSteamId) is { } manual)
		{
			var known = Candidates.FirstOrDefault(c => c.SteamId == manual);
			await _profiles.SelectOrAddAsync(manual, known?.Name ?? manual.ToString());
		}
		else if (_selected is { } candidate)
		{
			await _profiles.SelectOrAddAsync(candidate.SteamId, candidate.Name);
		}
		else
		{
			Message = "Pick a player from the list or paste a SteamID first.";
			return;
		}

		ManualSteamId = "";
		await Shell.Current.GoToAsync("//overview");
	}

	private async Task AddFolderAsync()
	{
		if (await DemoSources.PickFolderAsync() is not { } folder)
			return;

		_settings.AddDemoFolder(folder);
		await _queue.EnqueueFolderAsync(folder);
		await RefreshAsync();
	}

	private async Task AddFilesAsync()
	{
		await _queue.EnqueueAsync(await DemoSources.PickFilesAsync());
		await RefreshAsync();
	}

	private void RemoveFolder(string folder)
	{
		_settings.RemoveDemoFolder(folder);
		var item = Folders.FirstOrDefault(f => f.Path == folder);
		if (item is not null)
			Folders.Remove(item);
	}

	private void OnQueueChanged()
	{
		OnPropertyChanged(nameof(IsReading));
		OnPropertyChanged(nameof(ReadingText));
		OnPropertyChanged(nameof(ReadingProgress));
	}

	/// <summary>A SteamID64, or a steamcommunity.com/profiles/ link. Custom /id/ links would need the Steam Web API.</summary>
	public static ulong? ParseSteamId(string text)
	{
		var match = SteamId64().Match(text ?? "");
		return match.Success && ulong.TryParse(match.Value, out var id) ? id : null;
	}

	[GeneratedRegex(@"7656119\d{10}")]
	private static partial Regex SteamId64();
}
