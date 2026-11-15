using System.Collections.ObjectModel;
using System.Windows.Input;
using Gloria.Core.Classification;
using Gloria.Data.Import;
using Gloria.Data.Queries;
using Gloria.Graphics;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed record TileItem(string Label, string Value, string Delta, Color DeltaColor, SparklineDrawable Spark, string Foot);

/// <param name="Bar">Proportional bounds of this period's bar.</param>
/// <param name="Previous">Proportional x of the previous period's marker.</param>
public sealed record HabitItem(string Name, string Description, string Percent, Rect Bar, Rect Previous, bool HasPrevious);

public sealed class OverviewViewModel : ObservableObject
{
	private readonly GloriaReadModel _readModel;
	private readonly ProfileState _profiles;
	private readonly ImportQueue _queue;
	private readonly DemoImporter _importer;
	private string _subtitle = "";
	private string _olderText = "";
	private bool _hasData;
	private bool _isEmpty;

	public OverviewViewModel(GloriaReadModel readModel, ProfileState profiles, ImportQueue queue, DemoImporter importer)
	{
		_readModel = readModel;
		_profiles = profiles;
		_queue = queue;
		_importer = importer;
		ReanalyzeCommand = new Command(async () => await ReanalyzeAsync());
		_queue.DemoImported += async (_, _) => await RefreshAsync();
		_profiles.CurrentChanged += async (_, _) => await RefreshAsync();
	}

	public ObservableCollection<TileItem> Tiles { get; } = [];

	public ObservableCollection<HabitItem> Habits { get; } = [];

	public ObservableCollection<EncounterItem> Review { get; } = [];

	public ObservableCollection<MatchRow> Matches { get; } = [];

	public ICommand ReanalyzeCommand { get; }

	public ICommand AllMatchesCommand { get; } = new Command(async () => await Shell.Current.GoToAsync("//matches"));

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	public string OlderText { get => _olderText; private set => Set(ref _olderText, value); }

	public bool HasOlder => OlderText.Length > 0;

	public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }

	public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

	public string EmptyText => $"No encounters for {_profiles.Current?.Name ?? "this player"} yet. They appear as demos with this player are imported.";

	public async Task RefreshAsync()
	{
		if (_profiles.Current is not { } profile)
			return;

		var data = await _readModel.OverviewAsync(profile.SteamId);
		var encounters = data.Recent.Sum(d => d.Encounters);
		Subtitle = $"{profile.Name} · last {data.Recent.Count} of {data.DemoCount} demos · {encounters} encounters";
		OlderText = data.OlderVersionDemos == 0
			? ""
			: $"{data.OlderVersionDemos} demos were analyzed by an older version. Their numbers are left out until they are re-analyzed.";
		OnPropertyChanged(nameof(HasOlder));
		HasData = encounters > 0;
		IsEmpty = !HasData;
		OnPropertyChanged(nameof(EmptyText));

		Tiles.Clear();
		foreach (var tile in BuildTiles(data))
			Tiles.Add(tile);

		Habits.Clear();
		var recentTotal = Math.Max(1, encounters);
		var previousTotal = Math.Max(1, data.Previous.Sum(d => d.Encounters));
		foreach (var c in Enum.GetValues<AimCategory>())
		{
			var share = data.Recent.Sum(d => d.HabitCounts[(int)c]) / (double)recentTotal;
			var previous = data.Previous.Count == 0 ? -1 : data.Previous.Sum(d => d.HabitCounts[(int)c]) / (double)previousTotal;
			// Bars are drawn against 50%, so the common shares use most of the width.
			Habits.Add(new HabitItem(
				Display.Habit(c),
				Display.HabitDescription(c),
				$"{share:P0}",
				new Rect(0, 0, Math.Min(share * 2, 1), 1),
				new Rect(Math.Min(Math.Max(previous, 0) * 2, 1), 0, 2, 1),
				previous >= 0));
		}

		Review.Clear();
		foreach (var e in data.WorthALook)
			Review.Add(EncounterItem.From(e));

		Matches.Clear();
		foreach (var m in data.RecentMatches)
			Matches.Add(MatchRow.From(m));
	}

	private static IEnumerable<TileItem> BuildTiles(OverviewData data)
	{
		static double Ratio(int a, int b) => b == 0 ? double.NaN : (double)a / b;

		var metrics = new (string Label, string Foot, bool HigherIsBetter, Func<DemoStats, double> PerDemo, Func<IReadOnlyList<DemoStats>, double> Total, Func<double, string> Format)[]
		{
			("Duels won", "Kills ÷ (kills + deaths)", true,
				d => Ratio(d.Kills, d.Kills + d.Deaths),
				s => Ratio(s.Sum(d => d.Kills), s.Sum(d => d.Kills + d.Deaths)),
				v => $"{v:P0}"),
			("First-shot hit", "First bullet of the encounter hit", true,
				d => Ratio(d.FirstShotHits, d.FirstShotKnown),
				s => Ratio(s.Sum(d => d.FirstShotHits), s.Sum(d => d.FirstShotKnown)),
				v => $"{v:P0}"),
			("Median time to first shot", "From first sighting · ±0.5 s", false,
				d => d.MedianTimeToFirstShot ?? double.NaN,
				s => Median(s.Select(d => d.MedianTimeToFirstShot ?? double.NaN)),
				v => $"{v:0.00} s"),
			("Moving first shots", "Above the weapon's accurate speed", false,
				d => Ratio(d.MovingFirstShots, d.Encounters),
				s => Ratio(s.Sum(d => d.MovingFirstShots), s.Sum(d => d.Encounters)),
				v => $"{v:P0}"),
		};

		foreach (var m in metrics)
		{
			var now = m.Total(data.Recent);
			var before = data.Previous.Count == 0 ? double.NaN : m.Total(data.Previous);
			var delta = now - before;
			var better = delta > 0 == m.HigherIsBetter;
			var deltaText = double.IsNaN(delta)
				? "No earlier demos to compare"
				: Math.Abs(delta) < 1e-9 ? "Same as the previous demos"
				: $"{(delta > 0 ? "+" : "−")}{m.Format(Math.Abs(delta)).Replace("%", " pts")}, {(better ? "better" : "worse")}";
			yield return new TileItem(
				m.Label,
				double.IsNaN(now) ? "—" : m.Format(now),
				deltaText,
				double.IsNaN(delta) || Math.Abs(delta) < 1e-9 ? Display.Muted : better ? Display.Good : Color.FromArgb("#ec835a"),
				new SparklineDrawable { Values = data.Recent.Select(m.PerDemo).ToList() },
				m.Foot);
		}
	}

	private static double Median(IEnumerable<double> values)
	{
		var sorted = values.Where(v => !double.IsNaN(v)).Order().ToList();
		return sorted.Count == 0 ? double.NaN : sorted[sorted.Count / 2];
	}

	private async Task ReanalyzeAsync()
	{
		var paths = await _importer.RemoveOutdatedAsync();
		await _queue.EnqueueAsync(paths);
		await RefreshAsync();
	}
}
