using System.Collections.ObjectModel;
using Gloria.Core.Aggregation;
using Gloria.Core.Classification;
using Gloria.Core.Weapons;
using Gloria.Data;
using Gloria.Data.Queries;
using Gloria.Graphics;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed record TrendItem(string Title, string Now, string Range, SparklineDrawable Chart);

public sealed record WeaponRow(string Name, string Encounters, string Duels, string FirstShot, string MedianFirstShot, string Moving, string Habit);

/// <param name="Bar">Proportional bounds of the bar.</param>
public sealed record CauseRow(string Name, string Tag, string Percent, Rect Bar, Color Color);

/// <summary>
/// Trends across demos: per-demo values with a rolling median, the numbers per weapon class, and what cost the
/// duels the player lost, over a chosen number of recent demos.
/// </summary>
public sealed class TrendsViewModel : ObservableObject
{
	/// <summary>Demos in each rolling median. Odd, so the median is one demo's value.</summary>
	public const int RollingWindow = 5;

	private const string PeriodKey = "progress-period";

	private readonly GloriaReadModel _readModel;
	private readonly ProfileState _profiles;
	private readonly DuelOutcomes _outcomes;
	private readonly AppSettings _settings;
	private int? _period;
	private string _subtitle = "";
	private string _causesNote = "";
	private bool _hasCauses;
	private int _refresh;

	public TrendsViewModel(GloriaReadModel readModel, ProfileState profiles, DuelOutcomes outcomes, AppSettings settings)
	{
		_readModel = readModel;
		_profiles = profiles;
		_outcomes = outcomes;
		_settings = settings;
		_period = ChoiceOption.LoadPeriod(_settings, PeriodKey, fallback: null);
		Periods = ChoiceOption.Periods([20, 50, null], _period, SelectPeriod);
		_profiles.CurrentChanged += async (_, _) => await RefreshAsync();
	}

	public IReadOnlyList<ChoiceOption> Periods { get; }

	public ObservableCollection<TrendItem> Charts { get; } = [];

	public ObservableCollection<WeaponRow> Weapons { get; } = [];

	public ObservableCollection<CauseRow> Causes { get; } = [];

	public ObservableCollection<CauseRow> AimCauses { get; } = [];

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	public string CausesNote { get => _causesNote; private set => Set(ref _causesNote, value); }

	public bool HasCauses { get => _hasCauses; private set => Set(ref _hasCauses, value); }

	public string ChartNote => $"Dots are single demos; the line is the median of the last {RollingWindow} demos, so one odd match doesn't swing it.";

	public async Task RefreshAsync()
	{
		if (_profiles.Current is not { } profile)
			return;

		// A newer refresh (another period, another profile) wins over one still loading.
		var refresh = ++_refresh;
		var data = await _readModel.ProgressAsync(profile.SteamId, _period);
		if (refresh != _refresh)
			return;

		var demos = data.Demos;
		Subtitle = demos.Count == 0
			? $"{profile.Name} · no demos yet"
			: $"{profile.Name} · {demos.Count} demos · {Display.ShortDate(demos[0].PlayedAt)} – {Display.ShortDate(demos[^1].PlayedAt)} · {demos.Sum(d => d.Encounters)} encounters"
				+ (data.OlderVersionDemos > 0 ? $" · {data.OlderVersionDemos} older-version demos left out" : "");

		BuildCharts(demos);
		BuildWeapons(data.WeaponClasses);
		await BuildCausesAsync(data.DeathIds, refresh);
	}

	private void BuildCharts(IReadOnlyList<DemoStats> demos)
	{
		static double Ratio(int a, int b) => b == 0 ? double.NaN : (double)a / b;
		static string Percent(double v) => $"{v:P0}";

		var metrics = new List<(string Title, Func<DemoStats, double> Value, Func<double, string> Format)>
		{
			("Duels won", d => Ratio(d.Kills, d.Kills + d.Deaths), Percent),
			("First-shot hit", d => Ratio(d.FirstShotHits, d.FirstShotKnown), Percent),
			("Median time to first shot", d => d.MedianTimeToFirstShot ?? double.NaN, v => $"{v:0.00} s"),
			("Moving first shots", d => Ratio(d.MovingFirstShots, d.Encounters), Percent),
			("Clean encounters", d => Ratio(d.Clean, d.Encounters), Percent),
		};
		// Moving shot is left out: it is the moving-first-shots line above, unless corrections change it.
		foreach (var c in Enum.GetValues<AimCategory>().Where(c => c != AimCategory.MovingShot))
			metrics.Add((Display.Habit(c), d => Ratio(d.HabitCounts[(int)c], d.Encounters), Percent));

		Charts.Clear();
		foreach (var (title, value, format) in metrics)
		{
			var values = demos.Select(value).ToList();
			var trend = Rolling.Median(values, RollingWindow);
			var known = trend.Where(v => !double.IsNaN(v)).ToList();
			var range = known.Count switch
			{
				0 => "Not enough demos yet",
				1 => "Just enough demos for one median",
				_ => $"Started at {format(known[0])} · low {format(known.Min())} · high {format(known.Max())}",
			};
			Charts.Add(new TrendItem(title, known.Count == 0 ? "—" : format(known[^1]), range, new SparklineDrawable { Values = values, Trend = trend }));
		}
	}

	private void BuildWeapons(IReadOnlyList<WeaponClassStats> classes)
	{
		Weapons.Clear();
		foreach (var (weapon, s) in classes)
		{
			var habit = Enum.GetValues<AimCategory>()
				.Where(c => s.HabitCounts[(int)c] > 0)
				.OrderByDescending(c => s.HabitCounts[(int)c])
				.Select(c => $"{Display.Habit(c)} {Display.Percent(s.HabitCounts[(int)c], s.Encounters)}")
				.FirstOrDefault() ?? "—";
			Weapons.Add(new WeaponRow(
				Display.WeaponClass(weapon),
				s.Encounters.ToString("N0"),
				$"{Display.Percent(s.Kills, s.Kills + s.Deaths)} of {s.Kills + s.Deaths}",
				Display.Percent(s.FirstShotHits, s.FirstShotKnown),
				s.MedianTimeToFirstShot is { } t ? $"{t:0.00} s" : "—",
				Display.Percent(s.MovingFirstShots, s.Encounters),
				habit));
		}
	}

	private async Task BuildCausesAsync(IReadOnlyList<int> deathIds, int refresh)
	{
		Causes.Clear();
		AimCauses.Clear();
		if (!_outcomes.IsAvailable || deathIds.Count == 0)
		{
			HasCauses = false;
			CausesNote = _outcomes.IsAvailable ? "No lost duels in these demos." : "No outcome model is available, so lost duels can't be explained.";
			return;
		}

		CausesNote = $"Explaining {deathIds.Count:N0} lost duels…";
		var records = await _readModel.FeatureRecordsAsync(deathIds);
		var causes = await Task.Run(() => _outcomes.ExplainLosses(records));
		if (refresh != _refresh || causes is null)
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
