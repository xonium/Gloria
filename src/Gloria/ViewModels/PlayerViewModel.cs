using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
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

public sealed partial class PlayerViewModel : ObservableObject
{
	/// <summary>Candidates shown before searching: the most frequent, which usually include the user.</summary>
	private const int Shown = 12;

	private const int MaxMatches = 50;

	private readonly GloriaReadModel _readModel;
	private readonly ImportQueue _queue;
	private readonly ProfileState _profiles;
	private IReadOnlyList<PlayerCandidate> _all = [];
	private HashSet<ulong> _tracked = [];
	private int _demoCount;
	private CandidateItem? _selected;
	private string _manualSteamId = "";
	private string _search = "";
	private string _message = "";

	public PlayerViewModel(GloriaReadModel readModel, ImportQueue queue, ProfileState profiles)
	{
		_readModel = readModel;
		_queue = queue;
		_profiles = profiles;
		StartCommand = new Command(async () => await StartAsync());
		_queue.Changed += (_, _) => OnQueueChanged();
		_queue.DemoImported += async (_, _) => await RefreshAsync();
	}

	public ObservableCollection<CandidateItem> Candidates { get; } = [];

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

	public bool HasNoCandidates => _all.Count == 0;

	public bool HasCandidates => !HasNoCandidates;

	/// <summary>Filters the players by name, earlier names or SteamID.</summary>
	public string Search
	{
		get => _search;
		set
		{
			if (Set(ref _search, value))
				Show();
		}
	}

	/// <summary>What the list holds, e.g. "Showing 12 of 140 players · search to find the rest".</summary>
	public string ListText
	{
		get
		{
			var query = Search.Trim();
			if (query.Length > 0)
			{
				var found = _all.Count(c => Matches(c, query));
				return found switch
				{
					0 => $"No player matches \"{query}\".",
					> MaxMatches => $"Showing {MaxMatches} of {found} matches · type more to narrow it down",
					_ => found == 1 ? "1 match" : $"{found} matches",
				};
			}

			return _all.Count > Shown ? $"Showing {Shown} of {_all.Count} players · search to find the rest" : "";
		}
	}

	public bool HasListText => ListText.Length > 0;

	public async Task RefreshAsync()
	{
		_demoCount = await _readModel.DemoCountAsync();
		_all = await _readModel.PlayerCandidatesAsync();
		_tracked = _profiles.Profiles.Select(p => p.SteamId).ToHashSet();
		Show();

		if (_selected is null && _profiles.Current is null && Candidates.FirstOrDefault(c => c.Suggested) is { } suggested)
			Select(suggested);

		OnPropertyChanged(nameof(HasNoCandidates));
		OnPropertyChanged(nameof(HasCandidates));
		OnPropertyChanged(nameof(Title));
		OnPropertyChanged(nameof(Intro));
		OnQueueChanged();
	}

	/// <summary>Fills the list from all players: the most frequent, or those matching the search. Keeps the selection.</summary>
	private void Show()
	{
		var query = Search.Trim();
		var shown = query.Length == 0
			? _all.Take(Shown)
			: _all.Where(c => Matches(c, query)).Take(MaxMatches);
		var keep = _selected?.SteamId;
		Candidates.Clear();
		foreach (var c in shown)
		{
			Candidates.Add(new CandidateItem(c, _demoCount, _tracked.Contains(c.SteamId), Select)
			{
				Suggested = c == _all[0] && c.DemoCount == _demoCount && _demoCount > 1,
			});
		}

		_selected = Candidates.FirstOrDefault(c => c.SteamId == keep);
		if (_selected is not null)
			_selected.IsSelected = true;

		OnPropertyChanged(nameof(StartText));
		OnPropertyChanged(nameof(ListText));
		OnPropertyChanged(nameof(HasListText));
	}

	private static bool Matches(PlayerCandidate candidate, string query) =>
		candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
		|| candidate.OtherNames.Any(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
		|| candidate.SteamId.ToString().Contains(query, StringComparison.Ordinal);

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
			var known = _all.FirstOrDefault(c => c.SteamId == manual);
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
		Search = "";
		await Shell.Current.GoToAsync("//overview");
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
