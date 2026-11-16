using System.Collections.ObjectModel;
using System.Windows.Input;
using Gloria.Core.Weapons;
using Gloria.Data;
using Gloria.Data.Import;
using Gloria.Data.Queries;
using Gloria.Services;

namespace Gloria.ViewModels;

/// <summary>A metric where one player is clearly ahead, for the "where you lose ground" cards.</summary>
public sealed record GapItem(string Name, string Values, string Description, string Difference, Color DifferenceColor);

/// <summary>One metric for both players, side by side.</summary>
public sealed record CompareRow(string Name, string Foot, string You, string Them, string Difference, Color DifferenceColor);

/// <param name="YouBar">Proportional bounds of the user's bar.</param>
/// <param name="ThemBar">Proportional bounds of the other player's marker or bar.</param>
public sealed record CompareBarItem(string Name, string Description, string You, string Them, Rect YouBar, Rect ThemBar);

/// <summary>
/// The current profile next to another player: a pro bundled with the app (<see cref="ReferencePlayers"/>) or another
/// tracked profile. Ranks the metrics by how far behind the user is, so the biggest differences come first.
/// </summary>
public sealed class CompareViewModel : ObservableObject
{
	private const string PeriodKey = "compare-period";
	private const string TargetKey = "compare-target";
	private const string WeaponKey = "compare-weapon";

	/// <summary>Encounters below which a side's numbers swing a lot from match to match.</summary>
	private const int SmallSample = 100;

	/// <summary>Gaps smaller than this (in share points, see <see cref="Comparison.Gaps"/>) are too small to point out.</summary>
	private const double NotableGap = 0.02;

	private static readonly Color Worse = Color.FromArgb("#ec835a");

	private readonly GloriaReadModel _readModel;
	private readonly ProfileState _profiles;
	private readonly ReferencePlayers _references;
	private readonly DuelOutcomes _outcomes;
	private readonly AppSettings _settings;
	private readonly List<Target> _targets = [];
	private int? _period;
	private int? _weapon;
	private int _refresh;
	private int _selectedTargetIndex = -1;
	private string _subtitle = "";
	private string _sampleNote = "";
	private string _sourceNote = "";
	private string _themName = "";
	private string _emptyText = "";
	private string _causesNote = "";
	private bool _hasData;
	private bool _hasCauses;

	public CompareViewModel(GloriaReadModel readModel, ProfileState profiles, ReferencePlayers references, DuelOutcomes outcomes, AppSettings settings, ImportQueue queue)
	{
		_readModel = readModel;
		_profiles = profiles;
		_references = references;
		_outcomes = outcomes;
		_settings = settings;
		_period = ChoiceOption.LoadPeriod(settings, PeriodKey, fallback: 20);
		_weapon = int.TryParse(settings.Get(WeaponKey), out var weapon) ? weapon : null;
		Periods = ChoiceOption.Periods([10, 20, 50, null], _period, SelectPeriod);
		_profiles.CurrentChanged += async (_, _) => await RefreshAsync();
		_profiles.Profiles.CollectionChanged += async (_, _) => await RefreshAsync();
		_references.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
		queue.DemoImported += async (_, _) => await RefreshAsync();
	}

	public IReadOnlyList<ChoiceOption> Periods { get; }

	/// <summary>"All" and each weapon class both players fired first with.</summary>
	public ObservableCollection<ChoiceOption> Weapons { get; } = [];

	public ObservableCollection<string> TargetNames { get; } = [];

	public ObservableCollection<GapItem> Behind { get; } = [];

	public ObservableCollection<GapItem> Ahead { get; } = [];

	public ObservableCollection<CompareRow> Rows { get; } = [];

	public ObservableCollection<CompareBarItem> Habits { get; } = [];

	public ObservableCollection<CompareBarItem> Causes { get; } = [];

	public ICommand PickPlayerCommand { get; } = new Command(async () => await Shell.Current.GoToAsync("//player"));

	public int SelectedTargetIndex
	{
		get => _selectedTargetIndex;
		set
		{
			if (!Set(ref _selectedTargetIndex, value) || value < 0 || value >= _targets.Count)
				return;

			_settings.Set(TargetKey, _targets[value].Key);
			// Let the Picker's dropdown close before the page rebuilds, as the sidebar's profile picker does.
			MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
		}
	}

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	/// <summary>A warning when either side has few encounters; empty otherwise.</summary>
	public string SampleNote
	{
		get => _sampleNote;
		private set
		{
			if (Set(ref _sampleNote, value))
				OnPropertyChanged(nameof(HasSampleNote));
		}
	}

	public bool HasSampleNote => SampleNote.Length > 0;

	/// <summary>Where the other player's numbers come from, e.g. a pro's demos.</summary>
	public string SourceNote { get => _sourceNote; private set => Set(ref _sourceNote, value); }

	public string ThemName { get => _themName; private set => Set(ref _themName, value); }

	public string EmptyText
	{
		get => _emptyText;
		private set
		{
			if (Set(ref _emptyText, value))
				OnPropertyChanged(nameof(IsEmpty));
		}
	}

	public bool IsEmpty => EmptyText.Length > 0;

	public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }

	public bool HasAhead => Ahead.Count > 0;

	public bool HasBehind => Behind.Count > 0;

	public bool NothingBehind => !HasBehind;

	public string CausesNote { get => _causesNote; private set => Set(ref _causesNote, value); }

	public bool HasCauses { get => _hasCauses; private set => Set(ref _hasCauses, value); }

	public async Task RefreshAsync()
	{
		// A newer refresh (another player, period or weapon) wins over one still loading.
		var refresh = ++_refresh;
		if (_profiles.Current is not { } profile)
		{
			Show("Choose which player is you first, then compare yourself with a pro or a friend.");
			return;
		}

		var pros = await _references.AllAsync();
		if (refresh != _refresh)
			return;

		RebuildTargets(pros, profile.SteamId);
		if (_selectedTargetIndex < 0 || _selectedTargetIndex >= _targets.Count)
		{
			Show("No one to compare with yet. Track a friend from your demos on the Player page.");
			return;
		}

		var target = _targets[_selectedTargetIndex];
		var you = await _readModel.ComparisonSideAsync(profile.SteamId, profile.Name, ComparisonKind.You, _period);
		var them = target.Pro ?? await _readModel.ComparisonSideAsync(target.SteamId, target.Name, ComparisonKind.Friend, _period);
		if (refresh != _refresh)
			return;

		if (you.Overall.Encounters == 0)
		{
			Show($"No encounters for {profile.Name} yet. They appear as demos with this player are imported.");
			return;
		}

		RebuildWeapons(you, them);
		var weapon = _weapon is { } w ? (WeaponClass?)w : null;
		var a = you.StatsFor(weapon);
		var b = them.StatsFor(weapon);

		EmptyText = "";
		HasData = true;
		ThemName = them.Name;
		var with = weapon is { } c ? $" · {Display.WeaponClass(c).ToLowerInvariant()}" : "";
		Subtitle = $"{you.Name} · {Demos(you)} · {a.Encounters:N0} encounters{with}   vs   {them.Name} · {Demos(them)} · {b.Encounters:N0} encounters";
		SourceNote = them.Kind == ComparisonKind.Pro
			? $"{them.Name}: {them.Note}. Pros' numbers always cover all their bundled demos."
			: "";
		SampleNote = Math.Min(a.Encounters, b.Encounters) < SmallSample
			? $"Few encounters{with}: under {SmallSample} on one side, so differences of a few points may be chance."
			: "";

		var gaps = Comparison.Gaps(a, b);
		BuildGaps(gaps, them.Name);
		BuildRows(gaps);
		BuildHabits(gaps);
		await BuildCausesAsync(you, them, weapon, refresh);
	}

	private void Show(string empty)
	{
		EmptyText = empty;
		HasData = false;
		Subtitle = "";
		Behind.Clear();
		Ahead.Clear();
		Rows.Clear();
		Habits.Clear();
		Causes.Clear();
		HasCauses = false;
		OnPropertyChanged(nameof(HasAhead));
		OnPropertyChanged(nameof(HasBehind));
		OnPropertyChanged(nameof(NothingBehind));
	}

	private void RebuildTargets(IReadOnlyList<ComparisonSide> pros, ulong you)
	{
		var targets = pros
			.Select(p => new Target($"pro:{p.Name}", $"{p.Name} · pro", p.Name, 0, p))
			.Concat(_profiles.Profiles
				.Where(p => p.SteamId != you)
				.Select(p => new Target($"profile:{p.SteamId}", $"{p.Name} · tracked", p.Name, p.SteamId, null)))
			.ToList();

		var key = _selectedTargetIndex >= 0 && _selectedTargetIndex < _targets.Count
			? _targets[_selectedTargetIndex].Key
			: _settings.Get(TargetKey);
		if (!targets.Select(t => t.Label).SequenceEqual(TargetNames))
		{
			_targets.Clear();
			_targets.AddRange(targets);
			TargetNames.Clear();
			foreach (var t in targets)
				TargetNames.Add(t.Label);
		}

		var index = Math.Max(0, _targets.FindIndex(t => t.Key == key));
		_selectedTargetIndex = _targets.Count == 0 ? -1 : index;
		OnPropertyChanged(nameof(SelectedTargetIndex));
	}

	private void RebuildWeapons(ComparisonSide you, ComparisonSide them)
	{
		var shared = you.ByClass
			.Where(w => w.Class != WeaponClass.Other && them.ByClass.Any(t => t.Class == w.Class))
			.Select(w => (int?)w.Class)
			.ToList();
		if (_weapon is { } selected && !shared.Contains(selected))
			_weapon = null;

		Weapons.Clear();
		foreach (var value in shared.Prepend(null))
		{
			var label = value is { } v ? Display.WeaponClass((WeaponClass)v) : "All weapons";
			Weapons.Add(new ChoiceOption(label, value, SelectWeapon) { IsSelected = value == _weapon });
		}
	}

	private void BuildGaps(IReadOnlyList<MetricGap> gaps, string them)
	{
		// Duels won and reaction times say as much about the opponents, so they stay in the table but aren't ranked.
		gaps = gaps.Where(g => !g.DependsOnOpposition).ToList();
		Behind.Clear();
		foreach (var g in gaps.Where(g => g.Gap >= NotableGap).OrderByDescending(g => g.Gap).Take(3))
			Behind.Add(Gap(g, them));

		Ahead.Clear();
		foreach (var g in gaps.Where(g => g.Gap <= -NotableGap).OrderBy(g => g.Gap).Take(2))
			Ahead.Add(Gap(g, them));

		OnPropertyChanged(nameof(HasAhead));
		OnPropertyChanged(nameof(HasBehind));
		OnPropertyChanged(nameof(NothingBehind));
	}

	private void BuildRows(IReadOnlyList<MetricGap> gaps)
	{
		Rows.Clear();
		foreach (var g in gaps.Where(g => g.Habit is null))
			Rows.Add(new CompareRow(Name(g), Foot(g), Format(g, g.You), Format(g, g.Them), Difference(g), DifferenceColor(g)));
	}

	private void BuildHabits(IReadOnlyList<MetricGap> gaps)
	{
		Habits.Clear();
		foreach (var g in gaps.Where(g => g.Habit is not null))
		{
			Habits.Add(new CompareBarItem(
				Name(g),
				Foot(g),
				Format(g, g.You),
				Format(g, g.Them),
				new Rect(0, 0, Math.Clamp(g.You, 0, 1), 1),
				new Rect(Math.Clamp(g.Them, 0, 1), 0, 2, 1)));
		}
	}

	private async Task BuildCausesAsync(ComparisonSide you, ComparisonSide them, WeaponClass? weapon, int refresh)
	{
		Causes.Clear();
		HasCauses = false;
		if (!_outcomes.IsAvailable)
		{
			CausesNote = "No outcome model is available, so lost duels can't be explained.";
			return;
		}

		var yourLosses = you.LossesWith(weapon);
		var theirLosses = them.LossesWith(weapon);
		if (yourLosses.Count == 0 || theirLosses.Count == 0)
		{
			CausesNote = "Both players need lost duels here to compare what cost them.";
			return;
		}

		CausesNote = $"Explaining {yourLosses.Count + theirLosses.Count:N0} lost duels…";
		var (a, b) = await Task.Run(() => (_outcomes.ExplainLosses(yourLosses), _outcomes.ExplainLosses(theirLosses)));
		if (refresh != _refresh || a is null || b is null)
			return;

		double Share(LossCauses causes, string name) =>
			(causes.Causes.FirstOrDefault(c => c.Name == name)?.LargestAim ?? 0) / (double)Math.Max(1, causes.Duels);

		var rows = a.Causes.Concat(b.Causes)
			.Where(c => !c.IsSituation && c.LargestAim > 0)
			.Select(c => c.Name)
			.Distinct()
			.Select(name => (Name: name, You: Share(a, name), Them: Share(b, name)))
			.OrderByDescending(r => r.You - r.Them)
			.Take(6)
			.ToList();
		var largest = Math.Max(0.01, rows.Select(r => Math.Max(r.You, r.Them)).DefaultIfEmpty(0).Max());
		foreach (var r in rows)
		{
			Causes.Add(new CompareBarItem(
				r.Name,
				r.You > r.Them + 0.03 ? "Costs you more often" : r.Them > r.You + 0.03 ? "Costs them more often" : "About the same",
				$"{r.You:P0}",
				$"{r.Them:P0}",
				new Rect(0, 0, Math.Max(0.01, r.You / largest), 1),
				new Rect(0, 0, Math.Max(0.01, r.Them / largest), 1)));
		}

		HasCauses = Causes.Count > 0;
		CausesNote = $"From the outcome model, over {a.Duels:N0} lost duels of yours and {b.Duels:N0} of {them.Name}'s: "
			+ "how often each aim factor lowered the chance to win the most. Those costing you more than them come first.";
	}

	private static GapItem Gap(MetricGap g, string them) => new(
		Name(g),
		$"You {Format(g, g.You)} · {them} {Format(g, g.Them)}",
		Foot(g),
		Difference(g),
		DifferenceColor(g));

	private static string Name(MetricGap g) => g.Habit is { } h ? Display.Habit(h) : g.Name;

	private static string Foot(MetricGap g) => g.Habit is { } h ? Display.HabitDescription(h) : g.Name switch
	{
		"Duels won" => "Kills ÷ (kills + deaths) · depends on who you play against",
		"First-shot hit" => "First bullet of the encounter hit",
		"Median time to first shot" => "From first sighting · ±0.5 s · depends on who you play against",
		"Moving first shots" => "Above the weapon's accurate speed",
		"Clean encounters" => "No habit flagged",
		"Pre-aimed" => "Within 5° of the body before the flick",
		"Median pre-aim error" => "Crosshair off the body before the flick",
		_ => "",
	};

	private static string Format(MetricGap g, double value) => g.Unit switch
	{
		MetricUnit.Seconds => $"{value:0.00} s",
		MetricUnit.Degrees => $"{value:0.0}°",
		_ => $"{value:P0}",
	};

	/// <summary>The user's value minus the other's, e.g. "+5 pts, worse".</summary>
	private static string Difference(MetricGap g)
	{
		var delta = g.You - g.Them;
		if (Math.Abs(g.Gap) < 1e-9)
			return "Same";

		var size = g.Unit switch
		{
			MetricUnit.Seconds => $"{Math.Abs(delta):0.00} s",
			MetricUnit.Degrees => $"{Math.Abs(delta):0.0}°",
			_ => $"{Math.Abs(delta) * 100:0} pts",
		};
		return $"{(delta > 0 ? "+" : "−")}{size}, {(g.Gap > 0 ? "worse" : "better")}";
	}

	private static Color DifferenceColor(MetricGap g) =>
		Math.Abs(g.Gap) < NotableGap ? Display.Muted : g.Gap > 0 ? Worse : Display.Good;

	private static string Demos(ComparisonSide side) => side.Demos == 1 ? "1 demo" : $"{side.Demos} demos";

	private async void SelectPeriod(ChoiceOption option)
	{
		foreach (var p in Periods)
			p.IsSelected = p == option;
		_period = option.Value;
		ChoiceOption.SavePeriod(_settings, PeriodKey, option.Value);
		await RefreshAsync();
	}

	private async void SelectWeapon(ChoiceOption option)
	{
		_weapon = option.Value;
		_settings.Set(WeaponKey, option.Value?.ToString());
		await RefreshAsync();
	}

	/// <param name="Key">What is saved to come back to this player: a pro's name or a profile's SteamID.</param>
	/// <param name="Pro">The bundled player's numbers; null for a tracked profile, which is queried.</param>
	private sealed record Target(string Key, string Label, string Name, ulong SteamId, ComparisonSide? Pro);
}
