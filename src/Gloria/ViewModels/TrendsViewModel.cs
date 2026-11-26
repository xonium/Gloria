using System.Collections.ObjectModel;
using System.Windows.Input;
using Gloria.Core.Aggregation;
using Gloria.Core.Classification;
using Gloria.Core.Weapons;
using Gloria.Data;
using Gloria.Data.Entities;
using Gloria.Data.Queries;
using Gloria.Graphics;
using Gloria.Services;

namespace Gloria.ViewModels;

/// <summary>One metric's chart card.</summary>
/// <param name="Now">The latest median, with one player.</param>
/// <param name="Range">Where the median started and its low and high, with one player.</param>
/// <param name="IsMulti">Several players: <paramref name="Leader"/> and <paramref name="Improved"/> replace <paramref name="Now"/> and <paramref name="Range"/>.</param>
/// <param name="Note">A caveat about the metric, e.g. that it depends on the opposition; empty for none.</param>
/// <param name="Icon">A weapon icon shown before the title; null for none.</param>
public sealed record TrendItem(
	string Title,
	string Now,
	string Range,
	SparklineDrawable Chart,
	bool IsMulti = false,
	string Leader = "",
	Color? LeaderColor = null,
	string Improved = "",
	Color? ImprovedColor = null,
	string Note = "",
	string? Icon = null)
{
	public bool IsSingle => !IsMulti;

	public bool HasNote => Note.Length > 0;

	public bool HasIcon => Icon is not null;

	public Color LeaderDot => LeaderColor ?? Colors.Transparent;

	public Color ImprovedDot => ImprovedColor ?? Colors.Transparent;
}

/// <summary>
/// A row of the by-weapon table. With several players, each weapon class gets a header row (<paramref name="IsHeader"/>)
/// followed by one row per player.
/// </summary>
/// <param name="Player">The player's name on a player row; empty with one player and on header rows.</param>
public sealed record WeaponRow(
	string Name,
	string Encounters,
	string Duels,
	string FirstShot,
	string MedianFirstShot,
	string Moving,
	string Habit,
	bool IsHeader = false,
	string Player = "",
	Color? PlayerColor = null)
{
	public bool HasPlayer => Player.Length > 0;

	public bool IsClassRow => !IsHeader && !HasPlayer;

	public bool IsDataRow => !IsHeader;

	/// <summary>Player rows sit under their class's header, without a line between them.</summary>
	public bool HasDivider => !HasPlayer;

	public Thickness RowPadding => HasPlayer ? new Thickness(8, 6) : new Thickness(8, 12);

	public Color PlayerDot => PlayerColor ?? Colors.Transparent;
}

/// <param name="Bar">Proportional bounds of the bar.</param>
public sealed record CauseRow(string Name, string Tag, string Percent, Rect Bar, Color Color);

/// <summary>One player's share for a cause of lost duels.</summary>
/// <param name="Bar">Proportional bounds of the bar.</param>
public sealed record CauseBar(string Percent, Rect Bar, Color Color);

/// <summary>A cause of lost duels with a bar per player.</summary>
public sealed record CauseCompareRow(string Name, IReadOnlyList<CauseBar> Bars);

/// <summary>A player shown on a page that compares players, in their line color.</summary>
public sealed record PlayerChip(string Name, string Detail, Color Color, bool CanRemove, ICommand RemoveCommand)
{
	public string RemoveDescription => $"Remove {Name}";

	/// <summary>Room for the remove button on the right, or the same margin as on the left without one.</summary>
	public Thickness ChipPadding => CanRemove ? new Thickness(12, 0, 4, 0) : new Thickness(12, 0);
}

/// <summary>
/// Trends across demos: per-demo values with a rolling median, the numbers per weapon class, and what cost the
/// duels lost, over a chosen number of recent demos. Shows the viewed profile, and other tracked profiles added to
/// compare with: then each player gets a colored line, and each chart names the current leader and who improved most.
/// </summary>
public sealed class TrendsViewModel : ObservableObject
{
	/// <summary>Demos in each rolling median. Odd, so the median is one demo's value.</summary>
	public const int RollingWindow = 5;

	/// <summary>Counted kills with a gun, by one of the players, for it to get a time-to-kill card.</summary>
	public const int MinGunKills = 5;

	/// <summary>Time-to-kill cards at most, for the guns with the most kills.</summary>
	public const int MaxGunCharts = 6;

	/// <summary>Kills pooled from the recent demos before a gun's line has a point.</summary>
	public const int MinPooledKills = 3;

	private const string PeriodKey = "progress-period";
	private const string PlayersKey = "progress-players";

	private readonly GloriaReadModel _readModel;
	private readonly ProfileState _profiles;
	private readonly DuelOutcomes _outcomes;
	private readonly AppSettings _settings;
	private readonly ComparedPlayers _compared;
	private int? _period;
	private string _subtitle = "";
	private string _causesNote = "";
	private string _causesTitle = "";
	private string _weaponsNote = "";
	private string _gunNote = "";
	private bool _hasGunCharts;
	private bool _hasCauses;
	private bool _isMulti;
	private bool _canAdd;
	private int _refresh;

	public TrendsViewModel(GloriaReadModel readModel, ProfileState profiles, DuelOutcomes outcomes, AppSettings settings)
	{
		_readModel = readModel;
		_profiles = profiles;
		_outcomes = outcomes;
		_settings = settings;
		_period = ChoiceOption.LoadPeriod(_settings, PeriodKey, fallback: null);
		_compared = new ComparedPlayers(settings, profiles, PlayersKey);
		Periods = ChoiceOption.Periods([20, 50, null], _period, SelectPeriod);
		AddCommand = new Command(async () =>
		{
			if (await _compared.AskToAddAsync())
				await RefreshAsync();
		});
		_profiles.CurrentChanged += async (_, _) => await RefreshAsync();
		_profiles.Profiles.CollectionChanged += async (_, _) => await RefreshAsync();
	}

	public IReadOnlyList<ChoiceOption> Periods { get; }

	/// <summary>The players shown, the viewed profile first; also the charts' legend.</summary>
	public ObservableCollection<PlayerChip> Players { get; } = [];

	public ObservableCollection<TrendItem> Charts { get; } = [];

	/// <summary>Time to kill per gun, for the guns with enough kills.</summary>
	public ObservableCollection<TrendItem> GunCharts { get; } = [];

	public bool HasGunCharts { get => _hasGunCharts; private set => Set(ref _hasGunCharts, value); }

	public string GunNote { get => _gunNote; private set => Set(ref _gunNote, value); }

	public ObservableCollection<WeaponRow> Weapons { get; } = [];

	public ObservableCollection<CauseRow> Causes { get; } = [];

	public ObservableCollection<CauseRow> AimCauses { get; } = [];

	public ObservableCollection<CauseCompareRow> CompareCauses { get; } = [];

	/// <summary>Asks which tracked profile to add, then shows it.</summary>
	public ICommand AddCommand { get; }

	public bool CanAdd { get => _canAdd; private set => Set(ref _canAdd, value); }

	public bool IsMulti
	{
		get => _isMulti;
		private set
		{
			if (Set(ref _isMulti, value))
			{
				OnPropertyChanged(nameof(IsSingle));
				OnPropertyChanged(nameof(ChartNote));
				OnPropertyChanged(nameof(ShowSingleCauses));
				OnPropertyChanged(nameof(ShowCompareCauses));
			}
		}
	}

	public bool IsSingle => !IsMulti;

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	public string CausesTitle { get => _causesTitle; private set => Set(ref _causesTitle, value); }

	public string CausesNote { get => _causesNote; private set => Set(ref _causesNote, value); }

	public string WeaponsNote { get => _weaponsNote; private set => Set(ref _weaponsNote, value); }

	public bool HasCauses
	{
		get => _hasCauses;
		private set
		{
			if (Set(ref _hasCauses, value))
			{
				OnPropertyChanged(nameof(ShowSingleCauses));
				OnPropertyChanged(nameof(ShowCompareCauses));
			}
		}
	}

	public bool ShowSingleCauses => HasCauses && !IsMulti;

	public bool ShowCompareCauses => HasCauses && IsMulti;

	public string ChartNote => IsMulti
		? $"Each line is one player's median of their last {RollingWindow} demos. Everyone's latest demo is at the right edge, so the same spot can be a different day for each player."
		: $"Dots are single demos; the line is the median of the last {RollingWindow} demos, so one odd match doesn't swing it.";

	public async Task RefreshAsync()
	{
		if (_profiles.Current is not { } profile)
			return;

		// A newer refresh (another period, another profile) wins over one still loading.
		var refresh = ++_refresh;
		var players = _compared.Select(profile);
		CanAdd = _compared.CanAdd;
		var data = await Task.WhenAll(players.Select(p => _readModel.ProgressAsync(p.SteamId, _period)));
		if (refresh != _refresh)
			return;

		IsMulti = players.Count > 1;
		_compared.FillChips(Players, players, i => data[i].Demos.Count == 1 ? "1 demo" : $"{data[i].Demos.Count} demos", RefreshAsync);
		if (IsMulti)
		{
			var older = data.Sum(d => d.OlderVersionDemos);
			Subtitle = $"{players.Count} players · {(_period is { } n ? $"each one's last {n} demos" : "all their demos")}"
				+ (older > 0 ? $" · {older} older-version demos left out" : "");
		}
		else
		{
			var demos = data[0].Demos;
			Subtitle = demos.Count == 0
				? $"{profile.Name} · no demos yet"
				: $"{profile.Name} · {demos.Count} demos · {Display.ShortDate(demos[0].PlayedAt)} – {Display.ShortDate(demos[^1].PlayedAt)} · {demos.Sum(d => d.Encounters)} encounters"
					+ (data[0].OlderVersionDemos > 0 ? $" · {data[0].OlderVersionDemos} older-version demos left out" : "");
		}

		BuildCharts(players, data);
		BuildGunCharts(players, data);
		BuildWeapons(players, data);
		await BuildCausesAsync(players, data, refresh);
	}

	private void BuildCharts(IReadOnlyList<ProfileRecord> players, IReadOnlyList<ProgressData> data)
	{
		Charts.Clear();
		foreach (var metric in Comparison.Metrics)
		{
			var title = metric.Habit is { } h ? Display.Habit(h) : metric.Name;
			var values = data.Select(d => (IReadOnlyList<double>)d.Demos.Select(s => metric.Value(s.Stats)).ToList()).ToList();
			var trends = values.Select(v => (IReadOnlyList<double>)Rolling.Median(v, RollingWindow)).ToList();
			Charts.Add(Card(title, metric.Unit, metric.HigherIsBetter, players, values, trends,
				multiNote: metric.DependsOnOpposition ? "Depends on who they play against" : ""));
		}
	}

	/// <summary>
	/// A card per gun fired first that some player has <see cref="MinGunKills"/> counted kills with, most kills first.
	/// Kills with one gun are sparse, so each line is the median of the kills in the last <see cref="RollingWindow"/>
	/// demos rather than a median of per-demo medians.
	/// </summary>
	private void BuildGunCharts(IReadOnlyList<ProfileRecord> players, IReadOnlyList<ProgressData> data)
	{
		GunCharts.Clear();
		var guns = data
			.SelectMany(d => d.Guns)
			.GroupBy(g => g.Weapon)
			.Where(g => g.Any(x => x.Kills >= MinGunKills))
			.OrderByDescending(g => g.Sum(x => x.Kills))
			.Take(MaxGunCharts)
			.Select(g => g.Key)
			.ToList();

		foreach (var weapon in guns)
		{
			var mine = data.Select(d => d.Guns.FirstOrDefault(g => g.Weapon == weapon)).ToList();
			var samples = data
				.Select((d, i) => mine[i]?.SamplesPerDemo ?? d.Demos.Select(_ => (IReadOnlyList<double>)[]).ToList())
				.ToList();
			var values = samples.Select(s => (IReadOnlyList<double>)s.Select(Rolling.MedianOf).ToList()).ToList();
			var trends = samples.Select(s => (IReadOnlyList<double>)Rolling.PooledMedian(s, RollingWindow, MinPooledKills)).ToList();
			static string Kills(int n) => n == 1 ? "1 kill" : $"{n:N0} kills";
			GunCharts.Add(Card(
				Display.Weapon(weapon),
				MetricUnit.Seconds,
				higherIsBetter: false,
				players,
				values,
				trends,
				singleDetail: Kills(mine[0]?.Kills ?? 0),
				multiNote: string.Join(" · ", players.Select((p, i) => (p.Name, Kills: mine[i]?.Kills ?? 0)).Where(x => x.Kills > 0).Select(x => $"{x.Name} {Kills(x.Kills)}")),
				icon: Display.WeaponIcon(weapon)));
		}

		HasGunCharts = GunCharts.Count > 0;
		GunNote = HasGunCharts
			? $"From the first shot, hit or miss, to the kill, over kills on opponents at full health. Grouped by the gun fired first; guns with at least {MinGunKills} such kills. "
				+ (IsMulti
					? $"Each line is the median of a player's kills in their last {RollingWindow} demos."
					: $"Dots are single demos; the line is the median of the kills in the last {RollingWindow} demos.")
			: "";
	}

	/// <summary>
	/// A chart card: with one player the latest median and its range; with several, one line each and who leads and
	/// who improved most.
	/// </summary>
	/// <param name="values">Per player, the value of each demo (dots, shown with one player).</param>
	/// <param name="trends">Per player, the smoothed line.</param>
	/// <param name="singleDetail">Added to the range line with one player.</param>
	/// <param name="multiNote">The card's note with several players.</param>
	private static TrendItem Card(
		string title,
		MetricUnit unit,
		bool higherIsBetter,
		IReadOnlyList<ProfileRecord> players,
		IReadOnlyList<IReadOnlyList<double>> values,
		IReadOnlyList<IReadOnlyList<double>> trends,
		string singleDetail = "",
		string multiNote = "",
		string? icon = null)
	{
		string Format(double v) => Display.Metric(unit, v);

		if (players.Count == 1)
		{
			var known = trends[0].Where(v => !double.IsNaN(v)).ToList();
			var range = known.Count switch
			{
				0 => "Not enough demos yet",
				1 => "Just enough demos for one median",
				_ => $"Started at {Format(known[0])} · low {Format(known.Min())} · high {Format(known.Max())}",
			};
			if (singleDetail.Length > 0)
				range += $" · {singleDetail}";
			return new TrendItem(title, known.Count == 0 ? "—" : Format(known[^1]), range, new SparklineDrawable { Values = values[0], Trend = trends[0] }, Icon: icon);
		}

		var standing = ProgressRace.Rank(trends, higherIsBetter, RollingWindow);
		var leader = standing.Leaders.Count == 0
			? "Not enough demos yet"
			: $"{string.Join(" & ", standing.Leaders.Select(i => players[i].Name))} · {Format(standing.Best!.Value)}";
		var improved = standing.Improvement switch
		{
			Improvement.Improved => $"{players[standing.MostImproved!.Value].Name} · {Display.MetricChange(unit, standing.Change!.Value)}",
			Improvement.NoOneImproved => "No one improved",
			_ => "Not enough demos yet",
		};
		var chart = new SparklineDrawable { Series = trends.Select((t, i) => new SparkSeries(t, Display.SeriesColors[i])).ToList() };
		return new TrendItem(
			title,
			"",
			"",
			chart,
			IsMulti: true,
			leader,
			standing.Leaders.Count == 1 ? Display.SeriesColors[standing.Leaders[0]] : null,
			improved,
			standing.MostImproved is { } m ? Display.SeriesColors[m] : null,
			multiNote,
			icon);
	}

	private void BuildWeapons(IReadOnlyList<ProfileRecord> players, IReadOnlyList<ProgressData> data)
	{
		Weapons.Clear();
		if (players.Count == 1)
		{
			WeaponsNote = "Grouped by the weapon you fired first with.";
			foreach (var (weapon, s) in data[0].WeaponClasses)
				Weapons.Add(Row(Display.WeaponClass(weapon), s));
			return;
		}

		WeaponsNote = "Grouped by the weapon each player fired first with.";
		var classes = data
			.SelectMany(d => d.WeaponClasses)
			.GroupBy(w => w.Class)
			.OrderByDescending(g => g.Sum(w => w.Stats.Encounters))
			.Select(g => g.Key);
		foreach (var weapon in classes)
		{
			Weapons.Add(new WeaponRow(Display.WeaponClass(weapon), "", "", "", "", "", "", IsHeader: true));
			for (var i = 0; i < players.Count; i++)
			{
				if (data[i].WeaponClasses.FirstOrDefault(w => w.Class == weapon) is { } stats)
					Weapons.Add(Row("", stats.Stats) with { Player = players[i].Name, PlayerColor = Display.SeriesColors[i] });
			}
		}
	}

	private static WeaponRow Row(string name, EncounterStats s)
	{
		var habit = Enum.GetValues<AimCategory>()
			.Where(c => s.HabitCounts[(int)c] > 0)
			.OrderByDescending(c => s.HabitCounts[(int)c])
			.Select(c => $"{Display.Habit(c)} {Display.Percent(s.HabitCounts[(int)c], s.Encounters)}")
			.FirstOrDefault() ?? "—";
		return new WeaponRow(
			name,
			s.Encounters.ToString("N0"),
			$"{Display.Percent(s.Kills, s.Kills + s.Deaths)} of {s.Kills + s.Deaths}",
			Display.Percent(s.FirstShotHits, s.FirstShotKnown),
			s.MedianTimeToFirstShot is { } t ? $"{t:0.00} s" : "—",
			Display.Percent(s.MovingFirstShots, s.Encounters),
			habit);
	}

	private async Task BuildCausesAsync(IReadOnlyList<ProfileRecord> players, IReadOnlyList<ProgressData> data, int refresh)
	{
		Causes.Clear();
		AimCauses.Clear();
		CompareCauses.Clear();
		CausesTitle = players.Count == 1 ? "What cost your lost duels" : "What cost their lost duels";
		var deaths = data.Sum(d => d.DeathIds.Count);
		if (!_outcomes.IsAvailable || deaths == 0)
		{
			HasCauses = false;
			CausesNote = _outcomes.IsAvailable ? "No lost duels in these demos." : "No outcome model is available, so lost duels can't be explained.";
			return;
		}

		HasCauses = false;
		CausesNote = $"Explaining {deaths:N0} lost duels…";
		var explained = new List<LossCauses?>();
		foreach (var d in data)
		{
			IReadOnlyList<EncounterRecord> records = d.DeathIds.Count == 0 ? [] : await _readModel.FeatureRecordsAsync(d.DeathIds);
			explained.Add(records.Count == 0 ? null : await Task.Run(() => _outcomes.ExplainLosses(records)));
			if (refresh != _refresh)
				return;
		}

		if (players.Count == 1)
			ListSingleCauses(explained[0]);
		else
			ListCompareCauses(players, explained);
	}

	private void ListSingleCauses(LossCauses? causes)
	{
		if (causes is null)
			return;

		var largest = Math.Max(1, causes.Causes.Max(c => c.Largest));
		foreach (var c in causes.Causes.Where(c => c.Largest > 0).Take(8))
			Causes.Add(Row(c.Name, c.IsSituation, c.Largest, causes.Duels, largest));

		var aim = causes.Causes.Where(c => c.LargestAim > 0).OrderByDescending(c => c.LargestAim).ToList();
		var largestAim = Math.Max(1, aim.Select(c => c.LargestAim).DefaultIfEmpty(0).Max());
		foreach (var c in aim.Take(6))
			AimCauses.Add(Row(c.Name, false, c.LargestAim, causes.Duels, largestAim));

		HasCauses = Causes.Count > 0;
		CausesNote = $"From the outcome model, over {causes.Duels:N0} lost duels: how often each factor lowered the chance to win the most."
			+ " Situation factors are context, not mistakes; the right-hand list leaves them out, to show what to work on."
			+ (causes.WithAimCost < causes.Duels ? $" In {causes.Duels - causes.WithAimCost:N0} lost duels no aim factor counted against you." : "");
	}

	private void ListCompareCauses(IReadOnlyList<ProfileRecord> players, IReadOnlyList<LossCauses?> explained)
	{
		static double Share(LossCauses? causes, string name) => causes is null
			? 0
			: (causes.Causes.FirstOrDefault(c => c.Name == name)?.LargestAim ?? 0) / (double)Math.Max(1, causes.Duels);

		var rows = explained
			.Where(c => c is not null)
			.SelectMany(c => c!.Causes)
			.Where(c => !c.IsSituation && c.LargestAim > 0)
			.Select(c => c.Name)
			.Distinct()
			.Select(name => (Name: name, Shares: explained.Select(c => Share(c, name)).ToList()))
			.OrderByDescending(r => r.Shares.Max())
			.Take(6)
			.ToList();
		var largest = Math.Max(0.01, rows.SelectMany(r => r.Shares).DefaultIfEmpty(0).Max());
		foreach (var (name, shares) in rows)
		{
			var bars = shares
				.Select((s, i) => new CauseBar(
					explained[i] is null ? "—" : $"{s:P0}",
					new Rect(0, 0, Math.Max(0.01, s / largest), 1),
					Display.SeriesColors[i]))
				.ToList();
			CompareCauses.Add(new CauseCompareRow(name, bars));
		}

		HasCauses = CompareCauses.Count > 0;
		var counts = string.Join(", ", players.Select((p, i) => $"{p.Name} {explained[i]?.Duels ?? 0:N0}"));
		CausesNote = $"From the outcome model, over each player's lost duels ({counts}): how often each aim factor lowered the chance to win the most."
			+ " Situation factors are left out: they are context, not mistakes.";
	}

	private static CauseRow Row(string name, bool situation, int count, int duels, int largest) => new(
		name,
		situation ? "Situation" : "Aim",
		Display.Percent(count, duels),
		new Rect(0, 0, Math.Max(0.02, (double)count / largest), 1),
		situation ? Display.LineStrong : Display.Loss);

	private async void SelectPeriod(ChoiceOption option)
	{
		foreach (var p in Periods)
			p.IsSelected = p == option;
		_period = option.Value;
		ChoiceOption.SavePeriod(_settings, PeriodKey, option.Value);
		await RefreshAsync();
	}
}
