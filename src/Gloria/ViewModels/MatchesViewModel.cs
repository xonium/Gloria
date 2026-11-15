using System.Collections.ObjectModel;
using System.Windows.Input;
using Gloria.Core.Classification;
using Gloria.Data;
using Gloria.Data.Queries;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed class MatchesViewModel : ObservableObject
{
	private readonly GloriaReadModel _readModel;
	private readonly ProfileState _profiles;
	private IReadOnlyList<MatchSummary> _all = [];
	private int _total;
	private int _mapIndex;
	private int _habitIndex;
	private string _subtitle = "";

	public MatchesViewModel(GloriaReadModel readModel, ProfileState profiles, ImportQueue queue)
	{
		_readModel = readModel;
		_profiles = profiles;
		Results =
		[
			new ChoiceOption("All", null, SelectResult) { IsSelected = true },
			new ChoiceOption("Wins", ResultWins, SelectResult),
			new ChoiceOption("Losses", ResultLosses, SelectResult),
			new ChoiceOption("Draws", ResultDraws, SelectResult),
		];
		HabitOptions = ["Any main habit", .. Enum.GetValues<AimCategory>().Select(Display.Habit), "No habit flagged"];
		ClearFiltersCommand = new Command(ClearFilters);
		queue.DemoImported += async (_, _) => await RefreshAsync();
		_profiles.CurrentChanged += async (_, _) => await RefreshAsync();
	}

	public ObservableCollection<MatchRow> Matches { get; } = [];

	public IReadOnlyList<ChoiceOption> Results { get; }

	/// <summary>"All maps", then the maps the player has played, by name.</summary>
	public ObservableCollection<string> MapOptions { get; } = ["All maps"];

	/// <summary>"Any main habit", each habit, then "No habit flagged".</summary>
	public IReadOnlyList<string> HabitOptions { get; }

	public ICommand ClearFiltersCommand { get; }

	public int MapIndex
	{
		get => _mapIndex;
		set
		{
			if (Set(ref _mapIndex, value))
				ApplyFilters();
		}
	}

	public int HabitIndex
	{
		get => _habitIndex;
		set
		{
			if (Set(ref _habitIndex, value))
				ApplyFilters();
		}
	}

	public bool IsFiltered => MapIndex > 0 || HabitIndex > 0 || Results.Any(r => r.IsSelected && r.Value is not null);

	public bool HasNoMatches => Matches.Count == 0 && _all.Count > 0;

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	public async Task RefreshAsync()
	{
		if (_profiles.Current is not { } profile)
			return;

		_all = await _readModel.MatchesAsync(profile.SteamId);
		_total = await _readModel.DemoCountAsync();

		// Keep the chosen map if the player still has matches on it.
		var map = MapIndex > 0 && MapIndex < MapOptions.Count ? MapOptions[MapIndex] : null;
		var maps = _all.Select(m => m.MapName).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
		MapOptions.Clear();
		MapOptions.Add("All maps");
		foreach (var m in maps)
			MapOptions.Add(m);
		_mapIndex = map is null ? 0 : Math.Max(0, MapOptions.IndexOf(map));
		OnPropertyChanged(nameof(MapIndex));

		ApplyFilters();
	}

	private const int ResultWins = 1;
	private const int ResultLosses = 2;
	private const int ResultDraws = 3;

	private void ApplyFilters()
	{
		if (_profiles.Current is not { } profile)
			return;

		var map = MapIndex > 0 && MapIndex < MapOptions.Count ? MapOptions[MapIndex] : null;
		var result = Results.FirstOrDefault(r => r.IsSelected)?.Value;
		var shown = _all.Where(m =>
			(map is null || m.MapName == map)
			&& result switch
			{
				ResultWins => m.RoundsWon > m.RoundsLost,
				ResultLosses => m.RoundsWon < m.RoundsLost,
				ResultDraws => m.RoundsWon == m.RoundsLost,
				_ => true,
			}
			&& HabitMatches(m.MainHabit)).ToList();

		Matches.Clear();
		foreach (var m in shown)
			Matches.Add(MatchRow.From(m));

		Subtitle = $"{_total} demos imported · {profile.Name} is in {_all.Count} of them"
			+ (IsFiltered ? $" · showing {shown.Count}" : "");
		OnPropertyChanged(nameof(IsFiltered));
		OnPropertyChanged(nameof(HasNoMatches));
	}

	private bool HabitMatches(AimCategory? habit)
	{
		var categories = Enum.GetValues<AimCategory>();
		if (HabitIndex <= 0)
			return true;
		if (HabitIndex > categories.Length)
			return habit is null;
		return habit == categories[HabitIndex - 1];
	}

	private void SelectResult(ChoiceOption option)
	{
		foreach (var r in Results)
			r.IsSelected = r == option;
		ApplyFilters();
	}

	private void ClearFilters()
	{
		_mapIndex = 0;
		_habitIndex = 0;
		OnPropertyChanged(nameof(MapIndex));
		OnPropertyChanged(nameof(HabitIndex));
		SelectResult(Results[0]);
	}
}
