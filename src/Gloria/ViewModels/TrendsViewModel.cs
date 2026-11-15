using System.Collections.ObjectModel;
using Gloria.Core.Classification;
using Gloria.Data.Queries;
using Gloria.Graphics;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed record TrendItem(string Title, string Now, string Range, SparklineDrawable Chart);

/// <summary>Per-demo values over the latest demos. The spike draws them as plain lines; the design's rolling medians come later.</summary>
public sealed class TrendsViewModel : ObservableObject
{
	private readonly GloriaReadModel _readModel;
	private readonly ProfileState _profiles;
	private string _subtitle = "";

	public TrendsViewModel(GloriaReadModel readModel, ProfileState profiles)
	{
		_readModel = readModel;
		_profiles = profiles;
		_profiles.CurrentChanged += async (_, _) => await RefreshAsync();
	}

	public ObservableCollection<TrendItem> Charts { get; } = [];

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	public async Task RefreshAsync()
	{
		if (_profiles.Current is not { } profile)
			return;

		var data = await _readModel.OverviewAsync(profile.SteamId, window: 30);
		var demos = data.Recent;
		Subtitle = demos.Count == 0
			? $"{profile.Name} · no demos yet"
			: $"{profile.Name} · {demos.Count} demos · {Display.ShortDate(demos[0].PlayedAt)} – {Display.ShortDate(demos[^1].PlayedAt)} · {demos.Sum(d => d.Encounters)} encounters";

		static double Ratio(int a, int b) => b == 0 ? double.NaN : (double)a / b;
		var metrics = new (string Title, Func<DemoStats, double> Value, Func<double, string> Format)[]
		{
			("Duels won", d => Ratio(d.Kills, d.Kills + d.Deaths), v => $"{v:P0}"),
			("First-shot hit", d => Ratio(d.FirstShotHits, d.FirstShotKnown), v => $"{v:P0}"),
			("Median time to first shot", d => d.MedianTimeToFirstShot ?? double.NaN, v => $"{v:0.00} s"),
			("Moving first shots", d => Ratio(d.MovingFirstShots, d.Encounters), v => $"{v:P0}"),
			("Overshoot", d => Ratio(d.HabitCounts[(int)AimCategory.Overshoot], d.Encounters), v => $"{v:P0}"),
			("Hasty", d => Ratio(d.HabitCounts[(int)AimCategory.Hasty], d.Encounters), v => $"{v:P0}"),
		};

		Charts.Clear();
		foreach (var (title, value, format) in metrics)
		{
			var values = demos.Select(value).ToList();
			var known = values.Where(v => !double.IsNaN(v)).ToList();
			Charts.Add(new TrendItem(
				title,
				known.Count == 0 ? "—" : format(known[^1]),
				known.Count < 2 ? "" : $"first {format(known[0])} · lowest {format(known.Min())} · highest {format(known.Max())}",
				new SparklineDrawable { Values = values }));
		}
	}
}
