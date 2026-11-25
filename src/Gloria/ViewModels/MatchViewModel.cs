using System.Collections.ObjectModel;
using System.Windows.Input;
using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Data;
using Gloria.Data.Queries;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed record StatItem(string Label, string Value, string Sub);

public sealed record DotItem(Color Fill, Color Stroke, double Size);

public sealed class RoundCell(RoundSummary round, Action<RoundCell> select) : ObservableObject
{
	private bool _selected;
	private Command? _selectCommand;

	public RoundSummary Round { get; } = round;

	public string Number => Round.Number.ToString();

	public string Side => Display.Side(Round.Side);

	public string Result => Round.Won switch { true => "W", false => "L", _ => "–" };

	public Color ResultColor => Round.Won switch { true => Display.AccentLight, false => Display.LossText, _ => Display.Muted };

	public IReadOnlyList<DotItem> Dots { get; } = round.Outcomes.Select(o => o switch
	{
		EncounterOutcome.Kill => new DotItem(Display.Accent, Display.Accent, 9),
		EncounterOutcome.Death => new DotItem(Display.Loss, Display.Loss, 9),
		_ => new DotItem(Colors.Transparent, Display.Muted, 7),
	}).ToList();

	public string Description => $"Round {Round.Number}, {(Round.Won switch { true => "won", false => "lost", _ => "result unknown" })}, {Round.Outcomes.Count} encounters";

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

public sealed record CountItem(string Name, string Label, Rect Bar, Color Color);

public sealed record OpponentItem(string Name, string Line);

public sealed class MatchViewModel(GloriaReadModel readModel, ProfileState profiles, DemoDates dates) : ObservableObject, IQueryAttributable
{
	private MatchDetail? _detail;
	private bool _isEditingDate;
	private DateTime? _editDate;
	private TimeSpan? _editTime;
	private RoundCell? _selected;
	private string _title = "";
	private string _score = "";
	private string _result = "";
	private Color _resultBackground = Display.Panel2;
	private Color _resultForeground = Display.Text2;
	private string _subtitle = "";
	private string _roundTitle = "";

	public ObservableCollection<StatItem> Stats { get; } = [];

	public ObservableCollection<RoundCell> Rounds { get; } = [];

	public ObservableCollection<EncounterItem> Encounters { get; } = [];

	public ObservableCollection<CountItem> Habits { get; } = [];

	public ObservableCollection<OpponentItem> Opponents { get; } = [];

	public ICommand BackCommand { get; } = new Command(async () => await Shell.Current.GoToAsync("//matches"));

	public ICommand ShowAllCommand => new Command(() => Select(null));

	public ICommand EditDateCommand => new Command(StartEditingDate);

	public ICommand SaveDateCommand => new Command(async () => await SaveDateAsync());

	public ICommand CancelDateCommand => new Command(() => IsEditingDate = false);

	public ICommand ResetDateCommand => new Command(async () => await SetDateAsync(null));

	public bool IsEditingDate
	{
		get => _isEditingDate;
		private set
		{
			if (Set(ref _isEditingDate, value))
				OnPropertyChanged(nameof(IsNotEditingDate));
		}
	}

	public bool IsNotEditingDate => !IsEditingDate;

	/// <summary>The date being edited, in local time.</summary>
	public DateTime? EditDate { get => _editDate; set => Set(ref _editDate, value); }

	public TimeSpan? EditTime { get => _editTime; set => Set(ref _editTime, value); }

	public bool IsUserDate => _detail?.Summary.PlayedAtSource == PlayedAtSource.User;

	public string DateHint => _detail?.Summary.PlayedAtSource switch
	{
		PlayedAtSource.User => "You set this date. It is kept when the demo is re-analyzed.",
		PlayedAtSource.MatchInfo => "From the match info file Valve's download came with.",
		_ => "From the file's timestamps, which can be the day it was downloaded. Set the real date so trends are in the right order.",
	};

	public string Title { get => _title; private set => Set(ref _title, value); }

	public string Score { get => _score; private set => Set(ref _score, value); }

	public string Result { get => _result; private set => Set(ref _result, value); }

	public Color ResultBackground { get => _resultBackground; private set => Set(ref _resultBackground, value); }

	public Color ResultForeground { get => _resultForeground; private set => Set(ref _resultForeground, value); }

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	public string RoundTitle { get => _roundTitle; private set => Set(ref _roundTitle, value); }

	public async void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue("demo", out var value) && int.TryParse(value?.ToString(), out var demoId))
			await LoadAsync(demoId);
	}

	private void StartEditingDate()
	{
		var local = (_detail?.Summary.PlayedAt ?? DateTimeOffset.Now).ToLocalTime();
		EditDate = local.Date;
		EditTime = new TimeSpan(local.Hour, local.Minute, 0);
		IsEditingDate = true;
	}

	private async Task SaveDateAsync()
	{
		if (EditDate is not { } date)
			return;

		var local = date.Date + (EditTime ?? TimeSpan.Zero);
		await SetDateAsync(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)));
	}

	private async Task SetDateAsync(DateTimeOffset? playedAt)
	{
		if (_detail is not { } d)
			return;

		await dates.SetAsync(d.Summary.DemoId, playedAt);
		IsEditingDate = false;
		await ReloadAsync();
	}

	/// <summary>Loads the match again, keeping the selected round, e.g. after returning from a corrected encounter.</summary>
	public async Task ReloadAsync()
	{
		if (_detail is { } d)
			await LoadAsync(d.Summary.DemoId, _selected?.Round.Number);
	}

	private async Task LoadAsync(int demoId, int? round = null)
	{
		_detail = await readModel.MatchDetailAsync(demoId, profiles.SteamId);
		if (_detail is not { } d)
			return;

		var m = d.Summary;
		var row = MatchRow.From(m);
		Title = m.MapName;
		Score = $"{m.RoundsWon} – {m.RoundsLost}";
		Result = row.Result switch { "W" => "WIN", "L" => "LOSS", _ => "DRAW" };
		ResultBackground = row.ResultBackground;
		ResultForeground = row.ResultForeground;
		Subtitle = $"{profiles.Current?.Name} started {Display.Side(m.StartingSide)} · {d.Rounds.Count} rounds · {d.TickRate} tick · {m.FileName} · {Display.Date(m.PlayedAt)}{m.PlayedAtSource switch { PlayedAtSource.FileTime => " (file date)", PlayedAtSource.User => " (set by you)", _ => "" }}";
		OnPropertyChanged(nameof(IsUserDate));
		OnPropertyChanged(nameof(DateHint));

		var times = d.Encounters.Where(e => e.TimeToFirstShot is not null).Select(e => e.TimeToFirstShot!.Value).Order().ToList();
		var moving = d.Encounters.Count(e => e.SpeedRatio > 1);
		Stats.Clear();
		Stats.Add(new StatItem(
			"Kills / deaths",
			m.Kills is { } k && m.Deaths is { } dt ? $"{k} / {dt}" : "—",
			m.Kills is null ? "Re-analyze to count" : $"{Display.KillDeathRatio(m.KillDeathRatio)} K/D · {Display.DamagePerRound(m.DamagePerRound)} ADR"));
		Stats.Add(new StatItem("Encounters", m.Encounters.ToString(), $"{d.Opponents.Count} opponents"));
		Stats.Add(new StatItem("Duels won", $"{m.DuelKills} / {m.DuelKills + m.DuelDeaths}", Display.Percent(m.DuelKills, m.DuelKills + m.DuelDeaths)));
		Stats.Add(new StatItem("First-shot hit", $"{m.FirstShotHits} / {m.FirstShotKnown}", Display.Percent(m.FirstShotHits, m.FirstShotKnown)));
		Stats.Add(new StatItem("Median first shot", times.Count == 0 ? "—" : $"{times[times.Count / 2]:0.00} s", "after first sighting"));
		Stats.Add(new StatItem("Moving first shots", moving.ToString(), $"{Display.Percent(moving, m.Encounters)} of encounters"));

		Rounds.Clear();
		foreach (var r in d.Rounds)
			Rounds.Add(new RoundCell(r, Select));

		Habits.Clear();
		var total = Math.Max(d.Encounters.Count, 1);
		foreach (var c in Enum.GetValues<AimCategory>())
		{
			var count = d.Encounters.Count(e => e.Has(c));
			Habits.Add(new CountItem(Display.Habit(c), $"{count} of {d.Encounters.Count}", new Rect(0, 0, (double)count / total, 1), Display.Accent));
		}

		var clean = d.Encounters.Count(e => e.Flags == 0);
		Habits.Add(new CountItem("Clean", $"{clean} of {d.Encounters.Count}", new Rect(0, 0, (double)clean / total, 1), Display.Good));

		Opponents.Clear();
		foreach (var o in d.Opponents)
			Opponents.Add(new OpponentItem(o.Name, $"{o.Encounters} encounters · won {o.Won}, lost {o.Lost}"));

		Select(Rounds.FirstOrDefault(r => r.Round.Number == round) ?? Rounds.FirstOrDefault(r => r.Round.Outcomes.Count > 0));
	}

	private void Select(RoundCell? cell)
	{
		if (_selected is not null)
			_selected.IsSelected = false;
		_selected = cell;
		if (cell is not null)
			cell.IsSelected = true;

		var rows = _detail?.Encounters.Where(e => cell is null || e.RoundNumber == cell.Round.Number) ?? [];
		Encounters.Clear();
		foreach (var e in rows)
			Encounters.Add(EncounterItem.From(e));

		RoundTitle = cell is null
			? $"All encounters · {Encounters.Count}"
			: $"Round {cell.Round.Number} · {cell.Side} side · {(cell.Round.Won switch { true => "won", false => "lost", _ => "result unknown" })} · {Encounters.Count} encounters";
	}
}
