using System.Collections.ObjectModel;
using System.Windows.Input;
using Gloria.Data;
using Gloria.Data.Entities;
using Gloria.Data.Queries;
using Gloria.Graphics;
using Gloria.Services;

namespace Gloria.ViewModels;

/// <summary>One player's numbers with the chosen gun, under the spray chart.</summary>
public sealed record SprayRow(
	string Player,
	Color Color,
	string Sprays,
	string MedianLength,
	string HitRate,
	string FirstBullet,
	string KillBullet);

/// <summary>
/// Spray control per full-auto gun: per bullet of the magazine, how many sprays got that far and how often that
/// bullet hit, plus the bullet that killed most often. Like Progress, other tracked profiles can be added to
/// compare with, each in their own color.
/// </summary>
public sealed class SprayViewModel : ObservableObject
{
	private const string PeriodKey = "spray-period";
	private const string PlayersKey = "spray-players";
	private const string WeaponKey = "spray-weapon";

	private readonly GloriaReadModel _readModel;
	private readonly ProfileState _profiles;
	private readonly AppSettings _settings;
	private readonly ComparedPlayers _compared;
	private readonly List<string> _guns = [];
	private IReadOnlyList<ProfileRecord> _players = [];
	private IReadOnlyList<SprayProfile> _data = [];
	private int? _period;
	private int _selectedGunIndex = -1;
	private string _subtitle = "";
	private string _emptyText = "";
	private string? _gunIcon;
	private string _gunNote = "";
	private SprayChartDrawable _chart = new();
	private bool _hasData;
	private bool _canAdd;
	private bool _isMulti;
	private int _refresh;

	public SprayViewModel(GloriaReadModel readModel, ProfileState profiles, AppSettings settings, ImportQueue queue)
	{
		_readModel = readModel;
		_profiles = profiles;
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
		queue.DemoImported += async (_, _) => await RefreshAsync();
	}

	public IReadOnlyList<ChoiceOption> Periods { get; }

	/// <summary>The players shown, the viewed profile first; also the chart's legend.</summary>
	public ObservableCollection<PlayerChip> Players { get; } = [];

	public ICommand AddCommand { get; }

	public bool CanAdd { get => _canAdd; private set => Set(ref _canAdd, value); }

	public bool IsMulti { get => _isMulti; private set => Set(ref _isMulti, value); }

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	/// <summary>The guns anyone shown sprayed with, by display name, the viewed profile's most used first.</summary>
	public ObservableCollection<string> GunNames { get; } = [];

	public int SelectedGunIndex
	{
		get => _selectedGunIndex;
		set
		{
			if (!Set(ref _selectedGunIndex, value) || value < 0 || value >= _guns.Count)
				return;

			_settings.Set(WeaponKey, _guns[value]);
			// Let the Picker's dropdown close before the chart rebuilds, as Compare does.
			MainThread.BeginInvokeOnMainThread(BuildGun);
		}
	}

	public string? GunIcon { get => _gunIcon; private set { if (Set(ref _gunIcon, value)) OnPropertyChanged(nameof(HasGunIcon)); } }

	public bool HasGunIcon => GunIcon is not null;

	/// <summary>A new drawable per gun, so the GraphicsView redraws.</summary>
	public SprayChartDrawable Chart { get => _chart; private set => Set(ref _chart, value); }

	/// <summary>The magazine size and how the chart reads.</summary>
	public string GunNote { get => _gunNote; private set => Set(ref _gunNote, value); }

	public ObservableCollection<SprayRow> Rows { get; } = [];

	public bool HasData
	{
		get => _hasData;
		private set
		{
			if (Set(ref _hasData, value))
				OnPropertyChanged(nameof(IsEmpty));
		}
	}

	public bool IsEmpty => !HasData;

	public string EmptyText { get => _emptyText; private set => Set(ref _emptyText, value); }

	public string Explanation =>
		"A spray is one press of fire, so a burst started mid-magazine counts from bullet 1 again. "
		+ "The solid line is the share of sprays that reached each bullet; the dashed line is how often that bullet hit an enemy "
		+ $"(left out where fewer than {GunSpray.MinBulletsForHitRate} were fired). ▼ marks the bullet that killed most often.";

	public async Task RefreshAsync()
	{
		if (_profiles.Current is not { } profile)
			return;

		// A newer refresh (another period, another profile) wins over one still loading.
		var refresh = ++_refresh;
		var players = _compared.Select(profile);
		CanAdd = _compared.CanAdd;
		var data = await Task.WhenAll(players.Select(p => _readModel.SprayAsync(p.SteamId, _period)));
		if (refresh != _refresh)
			return;

		_players = players;
		_data = data;
		IsMulti = players.Count > 1;
		_compared.FillChips(Players, players, i => Demos(data[i].Demos), RefreshAsync);

		var older = data.Sum(d => d.OlderVersionDemos);
		var period = _period is { } n ? (IsMulti ? $"each one's last {n} demos" : $"last {n} demos") : (IsMulti ? "all their demos" : "all demos");
		Subtitle = (IsMulti ? $"{players.Count} players · {period}" : $"{profile.Name} · {Demos(data[0].Demos)} · {period}")
			+ (older > 0 ? $" · {older} older-version demos left out" : "");

		RebuildGuns(data);
		HasData = _guns.Count > 0;
		EmptyText = older > 0 && data.All(d => d.Demos == 0)
			? "Sprays are new in this version. Re-analyze your demos on the Overview page to see them."
			: "No sprays with a full-auto gun yet. Import demos where you played rifles, SMGs or machine guns.";
		BuildGun();
	}

	private void RebuildGuns(IReadOnlyList<SprayProfile> data)
	{
		var guns = data
			.SelectMany((d, i) => d.Guns.Select(g => (g.Weapon, Mine: i == 0 ? g.Sprays : 0, All: g.Sprays)))
			.GroupBy(g => g.Weapon)
			.OrderByDescending(g => g.Sum(x => x.Mine))
			.ThenByDescending(g => g.Sum(x => x.All))
			.Select(g => g.Key)
			.ToList();
		var current = _selectedGunIndex >= 0 && _selectedGunIndex < _guns.Count ? _guns[_selectedGunIndex] : _settings.Get(WeaponKey);
		if (!guns.SequenceEqual(_guns))
		{
			_guns.Clear();
			_guns.AddRange(guns);
			GunNames.Clear();
			foreach (var gun in guns)
				GunNames.Add(Display.Weapon(gun));
		}

		_selectedGunIndex = _guns.Count == 0 ? -1 : Math.Max(0, _guns.IndexOf(current ?? ""));
		OnPropertyChanged(nameof(SelectedGunIndex));
	}

	private void BuildGun()
	{
		Rows.Clear();
		if (_selectedGunIndex < 0 || _selectedGunIndex >= _guns.Count)
		{
			Chart = new SprayChartDrawable { Lines = [] };
			GunIcon = null;
			GunNote = "";
			return;
		}

		var weapon = _guns[_selectedGunIndex];
		var sprays = _data.Select(d => d.Gun(weapon)).ToList();
		var clip = sprays.OfType<GunSpray>().First().Clip;
		GunIcon = Display.WeaponIcon(weapon);
		GunNote = $"{Display.Weapon(weapon)} · {clip}-round magazine";
		Chart = new SprayChartDrawable
		{
			Clip = clip,
			Lines = sprays
				.Select((g, i) => g is null
					? null
					: new SprayLine(
						Enumerable.Range(1, clip).Select(g.Reach).ToList(),
						Enumerable.Range(1, clip).Select(g.HitRateAt).ToList(),
						g.KillBullet,
						Display.SeriesColors[i]))
				.OfType<SprayLine>()
				.ToList(),
		};

		for (var i = 0; i < _players.Count && i < sprays.Count; i++)
		{
			var g = sprays[i];
			Rows.Add(new SprayRow(
				_players[i].Name,
				Display.SeriesColors[i],
				Sprays(g?.Sprays ?? 0),
				g?.MedianLength is { } length ? (length == 1 ? "1 bullet" : $"{length} bullets") : "—",
				Share(g?.HitRate ?? double.NaN),
				Share(g?.HitRateAt(1) ?? double.NaN),
				g?.KillBullet is { } kill ? $"Bullet {kill}" : "—"));
		}
	}

	private static string Share(double value) => double.IsNaN(value) ? "—" : $"{value * 100:0}%";

	private static string Sprays(int n) => n == 1 ? "1 spray" : $"{n:N0} sprays";

	private static string Demos(int n) => n == 1 ? "1 demo" : $"{n} demos";

	private async void SelectPeriod(ChoiceOption option)
	{
		foreach (var p in Periods)
			p.IsSelected = p == option;
		_period = option.Value;
		ChoiceOption.SavePeriod(_settings, PeriodKey, option.Value);
		await RefreshAsync();
	}
}
