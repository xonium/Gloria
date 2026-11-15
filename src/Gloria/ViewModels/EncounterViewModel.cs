using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Gloria.Core.Analysis;
using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Metrics;
using Gloria.Core.Weapons;
using Gloria.Data.Corrections;
using Gloria.Data.Entities;
using Gloria.Data.Queries;
using Gloria.Graphics;
using Gloria.Ml;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed record VerdictItem(string Question, string Verdict, bool Flagged, string Text)
{
	public bool NotFlagged => !Flagged;
}

public sealed record ScoreItem(string Name, string Percent, Rect Bar, Color Color, bool Flagged);

/// <param name="Tag">"Aim" or "Situation": situation factors are context, not mistakes.</param>
public sealed record CostItem(string Text, string Tag, Rect Bar, Color Color);

/// <summary>A player's health at the replay's current tick, as a bar and a number.</summary>
public sealed class HealthView : ObservableObject
{
	private int _health = -1;

	public int Health
	{
		get => _health;
		set
		{
			if (Set(ref _health, value))
			{
				OnPropertyChanged(nameof(Text));
				OnPropertyChanged(nameof(Fraction));
				OnPropertyChanged(nameof(Color));
			}
		}
	}

	public string Text => Health < 0 ? "—" : $"{Health} HP";

	public double Fraction => Math.Clamp(Health, 0, 100) / 100.0;

	public Color Color => Display.HealthColor(Health);
}

/// <summary>A weapon as its icon and name; the icon is null when there is no file for it.</summary>
public sealed class WeaponView : ObservableObject
{
	private string? _weapon;
	private ImageSource? _icon;

	public string Name => Display.Weapon(_weapon ?? "");

	public ImageSource? Icon => _icon;

	public bool HasIcon => _icon is not null;

	public void Show(string? weapon)
	{
		if (_weapon == weapon)
			return;

		_weapon = weapon;
		_icon = Display.WeaponIcon(weapon) is { } file ? ImageSource.FromFile(file) : null;
		OnPropertyChanged(nameof(Name));
		OnPropertyChanged(nameof(Icon));
		OnPropertyChanged(nameof(HasIcon));
	}
}

public sealed record ShotRow(string Number, string At, string Result, string Offset, string BulletOffset, string Speed, string Moving);

/// <summary>One aim category on the correction card: what the model said, and the user's verdict if any.</summary>
public sealed class CorrectionItem : ObservableObject
{
	private readonly Func<CorrectionItem, bool?, Task> _set;
	private bool? _userValue;

	public CorrectionItem(AimCategory category, float probability, bool? userValue, Func<CorrectionItem, bool?, Task> set)
	{
		Category = category;
		_userValue = userValue;
		_set = set;
		var flagged = probability >= 0.5f;
		ModelText = $"Model: {(flagged ? "yes" : "no")}, {probability:P0}";
		// Pressing the chosen answer again clears it, back to the model's verdict.
		YesCommand = new Command(async () => await _set(this, UserValue == true ? null : true));
		NoCommand = new Command(async () => await _set(this, UserValue == false ? null : false));
	}

	public AimCategory Category { get; }

	public string Name => Display.Habit(Category);

	public string Description => Display.HabitDescription(Category);

	public string ModelText { get; }

	public Command YesCommand { get; }

	public Command NoCommand { get; }

	public bool? UserValue
	{
		get => _userValue;
		set
		{
			if (Set(ref _userValue, value))
			{
				OnPropertyChanged(nameof(YesBackground));
				OnPropertyChanged(nameof(YesForeground));
				OnPropertyChanged(nameof(NoBackground));
				OnPropertyChanged(nameof(NoForeground));
			}
		}
	}

	public Color YesBackground => UserValue == true ? Display.AccentSoft : Colors.Transparent;

	public Color YesForeground => UserValue == true ? Display.AccentLight : Display.Text2;

	public Color NoBackground => UserValue == false ? Display.AccentSoft : Colors.Transparent;

	public Color NoForeground => UserValue == false ? Display.AccentLight : Display.Text2;
}

public sealed class SpeedOption(double value, Action<SpeedOption> select) : ObservableObject
{
	private bool _selected;
	private Command? _selectCommand;

	public double Value { get; } = value;

	public string Label => $"{Value:0.##}×";

	public Command SelectCommand => _selectCommand ??= new Command(() => select(this));

	public bool IsSelected
	{
		get => _selected;
		set
		{
			if (Set(ref _selected, value))
			{
				OnPropertyChanged(nameof(Background));
				OnPropertyChanged(nameof(Foreground));
			}
		}
	}

	public Color Background => IsSelected ? Display.Line : Colors.Transparent;

	public Color Foreground => IsSelected ? Display.Text : Display.Text2;
}

public sealed class EncounterViewModel : ObservableObject, IQueryAttributable
{
	private readonly GloriaReadModel _readModel;
	private readonly GameArt _art;
	private readonly DuelOutcomes _outcomes;
	private readonly CorrectionStore _corrections;
	private readonly Stopwatch _clock = new();
	private IDispatcherTimer? _timer;
	private EncounterDetail? _detail;
	private EncounterTrace? _trace;
	private int _startIndex;
	private float _accurateSpeed = 1;
	private double _position;
	private double _maxPosition = 1;
	private double _speed = 0.5;
	private bool _isPlaying;
	private string _title = "";
	private string _subtitle = "";
	private string _breadcrumb = "";
	private string _outcome = "";
	private Color _outcomeColor = Display.Muted;
	private string _timeText = "";
	private string _phaseText = "";
	private string _crosshairText = "";
	private string _speedText = "";
	private string _shotsText = "";
	private string _lastHitText = "";
	private string _markers = "";
	private string _modelNote = "";
	private string _outlookTitle = "";
	private string _winChance = "";
	private string _hitChance = "";
	private string _outlookNote = "";
	private bool _hasWinChance;
	private bool _hasCosts;
	private bool _hasTrace;
	private bool _hasBullets;

	public EncounterViewModel(GloriaReadModel readModel, DuelOutcomes outcomes, CorrectionStore corrections, UserModelTraining training, GameArt art)
	{
		_readModel = readModel;
		_art = art;
		_outcomes = outcomes;
		_corrections = corrections;
		Training = training;
		RetrainCommand = new Command(async () => await Training.RetrainAsync());
		PlayCommand = new Command(TogglePlay);
		StepBackCommand = new Command(() => Step(-1));
		StepForwardCommand = new Command(() => Step(1));
		PreviousCommand = new Command(async () => await OpenSibling(_detail?.PreviousId));
		NextCommand = new Command(async () => await OpenSibling(_detail?.NextId));
		BackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
		Speeds = [new SpeedOption(0.25, SelectSpeed), new SpeedOption(0.5, SelectSpeed) { IsSelected = true }, new SpeedOption(1, SelectSpeed)];
	}

	/// <summary>Raised when the drawables changed and the graphics views must repaint.</summary>
	public event EventHandler? Redraw;

	public ReplayDrawable Replay { get; } = new();

	public MapDrawable Map { get; } = new();

	public TraceChartDrawable ErrorChart { get; } = new() { FormatY = v => $"{Math.Round(v, 1) + 0:0.#}°" };

	public TraceChartDrawable SpeedChart { get; } = new() { MinY = 0, MaxY = 260 };

	public IReadOnlyList<SpeedOption> Speeds { get; }

	public ObservableCollection<VerdictItem> Verdicts { get; } = [];

	public ObservableCollection<ScoreItem> Scores { get; } = [];

	public ObservableCollection<ShotRow> Shots { get; } = [];

	public ObservableCollection<CostItem> Costs { get; } = [];

	public ObservableCollection<CorrectionItem> CorrectionItems { get; } = [];

	/// <summary>Correction counts and retraining state, bound directly by the card.</summary>
	public UserModelTraining Training { get; }

	public ICommand RetrainCommand { get; }

	public string CorrectionHint =>
		$"Your answers override the model here and count {new AimTrainingOptions().CorrectionWeight:0}× when your model is retrained,"
		+ $" which happens on its own after {UserModelTraining.RetrainAfter} new answers.";

	public ICommand PlayCommand { get; }

	public ICommand StepBackCommand { get; }

	public ICommand StepForwardCommand { get; }

	public ICommand PreviousCommand { get; }

	public ICommand NextCommand { get; }

	public ICommand BackCommand { get; }

	public string Title { get => _title; private set => Set(ref _title, value); }

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	public string Breadcrumb { get => _breadcrumb; private set => Set(ref _breadcrumb, value); }

	public string Outcome { get => _outcome; private set => Set(ref _outcome, value); }

	public Color OutcomeColor { get => _outcomeColor; private set => Set(ref _outcomeColor, value); }

	public string TimeText { get => _timeText; private set => Set(ref _timeText, value); }

	public string PhaseText { get => _phaseText; private set => Set(ref _phaseText, value); }

	public string CrosshairText { get => _crosshairText; private set => Set(ref _crosshairText, value); }

	public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }

	public string ShotsText { get => _shotsText; private set => Set(ref _shotsText, value); }

	public HealthView OpponentHealth { get; } = new();

	public HealthView PlayerHealth { get; } = new();

	public string LastHitText { get => _lastHitText; private set => Set(ref _lastHitText, value); }

	public WeaponView PlayerWeapon { get; } = new();

	public WeaponView OpponentWeapon { get; } = new();

	public string Markers { get => _markers; private set => Set(ref _markers, value); }

	public string ModelNote { get => _modelNote; private set => Set(ref _modelNote, value); }

	public bool HasTrace { get => _hasTrace; private set => Set(ref _hasTrace, value); }

	public bool HasOutlook => _outcomes.IsAvailable;

	public string OutlookTitle { get => _outlookTitle; private set => Set(ref _outlookTitle, value); }

	public string WinChance { get => _winChance; private set => Set(ref _winChance, value); }

	public bool HasWinChance { get => _hasWinChance; private set => Set(ref _hasWinChance, value); }

	public string HitChance { get => _hitChance; private set => Set(ref _hitChance, value); }

	public bool HasCosts { get => _hasCosts; private set => Set(ref _hasCosts, value); }

	public string OutlookNote { get => _outlookNote; private set => Set(ref _outlookNote, value); }

	/// <summary>The trace has bullet directions (crosshair plus recoil); older traces only have the crosshair.</summary>
	public bool HasBullets
	{
		get => _hasBullets;
		private set
		{
			if (Set(ref _hasBullets, value))
			{
				OnPropertyChanged(nameof(ReplayCaption));
				OnPropertyChanged(nameof(HitLegend));
				OnPropertyChanged(nameof(MissLegend));
			}
		}
	}

	public string ReplayCaption => "Through your eyes, relative to the opponent: the body is drawn to scale at this distance and stays still while your crosshair moves. "
		+ (HasBullets
			? "At each shot, the ring is where your crosshair was and the diamond is where the bullet went: the crosshair plus recoil. Random spread isn't in the demo, so the real bullet can land a little off that point."
			: "Shots are drawn where the crosshair was on the tick before. This demo was analyzed before recoil was stored; re-analyze it to see where the bullets went.");

	public string HitLegend => HasBullets ? "Bullet, hit" : "Shot, hit";

	public string MissLegend => HasBullets ? "Bullet, missed" : "Shot, missed";

	public double MaxPosition { get => _maxPosition; private set => Set(ref _maxPosition, value); }

	public bool IsPlaying
	{
		get => _isPlaying;
		private set
		{
			if (Set(ref _isPlaying, value))
				OnPropertyChanged(nameof(PlayText));
		}
	}

	public string PlayText => IsPlaying ? "Pause" : "Play";

	/// <summary>Sample index into the trace, fractional while playing.</summary>
	public double Position
	{
		get => _position;
		set
		{
			if (Set(ref _position, Math.Clamp(value, 0, MaxPosition)))
				UpdateFrame();
		}
	}

	public async void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue("id", out var value) && int.TryParse(value?.ToString(), out var id))
			await LoadAsync(id);
	}

	public void Stop()
	{
		_timer?.Stop();
		IsPlaying = false;
	}

	private async Task LoadAsync(int id)
	{
		Stop();
		_detail = await _readModel.EncounterDetailAsync(id);
		if (_detail is not { } d)
			return;

		var e = d.Record;
		Title = $"Round {e.RoundNumber} · vs {d.OpponentName}";
		Breadcrumb = $"{d.MapName} · {Display.ShortDate(d.PlayedAt)} / Round {e.RoundNumber} / vs {d.OpponentName}";
		Outcome = Display.Outcome(e.Outcome);
		OutcomeColor = Display.OutcomeColor(e.Outcome);
		var seconds = (e.EndTick - e.StartTick) / (double)Math.Max(d.TickRate, 1);
		Subtitle = $"{Display.Clock(d.SecondsIntoRound)} into the round · {Display.Weapon(e.FirstShotWeapon)} vs {Display.Weapon(e.OpponentWeapon)}"
			+ $" · lasted {seconds:0.00} s · {e.DistanceAtFirstShot:N0} units apart · {e.PlayerHealthAtStart} vs {e.OpponentHealthAtStart} HP"
			+ (e.FiredFirst ? " · you fired first" : " · they fired first");

		_accurateSpeed = StaticWeaponData.Instance.Find(e.FirstShotWeapon)?.AccurateSpeedThreshold ?? 0;
		BuildVerdicts(e);
		BuildScores(e);
		BuildOutlook(e);
		await BuildCorrectionsAsync(e);
		Map.Art = await _art.MapAsync(d.MapName);
		Map.MapName = d.MapName;
		Replay.Agent = d.Trace?.Scene is { } scene
			? await _art.AgentAsync(scene.OpponentTeam == Core.Ingestion.Team.CounterTerrorist ? "ct" : "t")
			: null;
		LoadTrace(d);
	}

	private void LoadTrace(EncounterDetail d)
	{
		_trace = d.Trace;
		HasTrace = _trace is { Count: > 1 };
		HasBullets = _trace?.HasBullets ?? false;
		Shots.Clear();
		if (_trace is not { Count: > 1 } trace)
			return;

		var e = d.Record;
		_startIndex = trace.IndexOf(e.StartTick);
		var seconds = Enumerable.Range(0, trace.Count).Select(i => trace.SecondsAt(i, e.StartTick)).ToArray();
		var shots = trace.Shots.Select(s => (trace.IndexOf(s.Tick - 1), s.Hit)).ToList();

		Replay.Trace = trace;
		Replay.StartIndex = _startIndex;
		Replay.OpponentLabel = $"{d.OpponentName} · {trace.Distance[_startIndex]:N0} u";
		Map.Trace = trace;
		Map.StartIndex = _startIndex;
		Map.OpponentLabel = d.OpponentName;

		ErrorChart.Seconds = seconds;
		ErrorChart.Values = trace.BodyError;
		ErrorChart.MinY = Math.Min(trace.BodyError.Min() - 1, -2);
		ErrorChart.MaxY = Math.Max(trace.BodyError.Max() + 1, 4);
		ErrorChart.Threshold = 0;
		ErrorChart.ThresholdLabel = "On the body";
		ErrorChart.Shots = shots;

		SpeedChart.Seconds = seconds;
		SpeedChart.Values = trace.Speed;
		SpeedChart.MaxY = Math.Max(260, trace.Speed.Max() + 10);
		SpeedChart.Threshold = _accurateSpeed > 0 ? _accurateSpeed : null;
		SpeedChart.ThresholdLabel = _accurateSpeed > 0 ? $"Accurate below {_accurateSpeed:0} u/s ({Display.Weapon(e.FirstShotWeapon)})" : null;
		SpeedChart.Shots = shots;

		var n = 0;
		foreach (var shot in trace.Shots)
		{
			var i = trace.IndexOf(shot.Tick - 1);
			var speed = trace.Speed[i];
			var offset = trace.BodyError[i];
			Shots.Add(new ShotRow(
				(++n).ToString(),
				$"{trace.SecondsAt(trace.IndexOf(shot.Tick), e.StartTick):0.00} s",
				shot.Hit ? $"Hit · {Display.HitGroup(shot.HitGroup)}{(shot.Damage is { } dealt ? $" · −{dealt}" : "")}" : "Missed",
				Math.Abs(offset) < 0.05f ? "on" : $"{Math.Abs(offset):0.0}°{(offset < 0 ? " past" : "")}",
				shot.BulletBodyError switch { null => "—", < 0.05f => "on", { } b => $"{b:0.0}°" },
				$"{speed:0} u/s",
				_accurateSpeed > 0 && speed > _accurateSpeed ? $"Yes, {speed / _accurateSpeed:0.00}×" : "No"));
		}

		Markers = BuildMarkers(trace, e);

		MaxPosition = trace.Count - 1;
		// Start on the first shot, where most of the story is visible.
		_position = -1;
		Position = trace.Shots.Count > 0 ? trace.IndexOf(trace.Shots[0].Tick - 1) : _startIndex;
	}

	/// <summary>
	/// The timeline under the replay: first sighting, shots, other damage between the two (utility, the knife,
	/// damage taken) and how it ended.
	/// </summary>
	private static string BuildMarkers(EncounterTrace trace, EncounterRecord e)
	{
		string At(int tick) => $"{trace.SecondsAt(trace.IndexOf(tick), e.StartTick):0.00} s";
		var events = new List<(int Tick, string Text)> { (e.StartTick, "Seen 0.00 s") };
		events.AddRange(trace.Shots.Select((s, k) =>
			(s.Tick, $"Shot {k + 1} {At(s.Tick)}{(s.Hit ? s.Damage is { } d ? $" (−{d})" : "" : " (miss)")}")));
		if (trace.Scene is { } scene)
		{
			var shotTicks = trace.Shots.Where(s => s.Hit).Select(s => s.Tick).ToHashSet();
			foreach (var damage in scene.Damage)
			{
				if (damage.ByPlayer && shotTicks.Contains(damage.Tick))
					continue;

				events.Add((damage.Tick, damage.ByPlayer
					? $"{Display.Weapon(damage.Weapon)} −{damage.Damage} {At(damage.Tick)}"
					: $"Took −{damage.Damage} ({Display.Weapon(damage.Weapon)}) {At(damage.Tick)}"));
			}
		}

		events.Add((int.MaxValue, $"{OutcomeText(trace, e)} {trace.SecondsAt(trace.Count - 1, e.StartTick):0.00} s"));
		return string.Join("   ·   ", events.OrderBy(x => x.Tick).Select(x => x.Text));
	}

	/// <summary>"Kill · HE grenade" or "Death · AWP · headshot" when the trace knows the deciding death.</summary>
	private static string OutcomeText(EncounterTrace trace, EncounterRecord e) =>
		trace.Scene?.Death is { } death
			? $"{Display.Outcome(e.Outcome)} · {Display.Weapon(death.Weapon)}{(death.Headshot ? " · headshot" : "")}"
			: Display.Outcome(e.Outcome);

	private void UpdateFrame()
	{
		if (_trace is not { } trace || _detail is not { } d)
			return;

		var i = Math.Clamp((int)Position, 0, trace.Count - 1);
		Replay.Position = (float)Position;
		Map.Position = (float)Position;
		ErrorChart.Playhead = (float)Position;
		SpeedChart.Playhead = (float)Position;

		var e = d.Record;
		var t = trace.SecondsAt(i, e.StartTick);
		TimeText = $"{(t < 0 ? "−" : "")}{Math.Abs(t):0.00} s";
		// Same sample the replay draws a shot at: the tick before it fired.
		var fired = trace.Shots.Where(s => trace.IndexOf(s.Tick - 1) <= i).ToList();
		var error = trace.BodyError[i];
		PhaseText = i < _startIndex ? "Not spotted yet; may already be in view"
			: i >= trace.Count - 1 ? OutcomeText(trace, e)
			: fired.Count > 0 ? $"Shooting: {fired.Count} fired, {fired.Count(s => s.Hit)} hit"
			: error <= 0.05f && error >= -0.05f ? "Crosshair on the body"
			: error < 0 ? "Past the body: overshoot"
			: "Moving toward the target";
		CrosshairText = Math.Abs(error) < 0.05f ? "On the body" : $"{Math.Abs(error):0.0}° {(error < 0 ? "past the body" : "off the body")}";
		SpeedText = _accurateSpeed > 0
			? $"{trace.Speed[i]:0} u/s · {trace.Speed[i] / _accurateSpeed:0.00}× accurate"
			: $"{trace.Speed[i]:0} u/s";
		ShotsText = $"{fired.Count} of {trace.Shots.Count} · {fired.Count(s => s.Hit)} hit";

		// Shots show on the tick before they fire, so damage does too: a hit lands with its diamond.
		var tick = trace.StartTick + i + 1;
		if (trace.Scene is { } scene)
		{
			var next = Math.Min(i + 1, trace.Count - 1);
			OpponentHealth.Health = scene.OpponentHealth[next];
			PlayerHealth.Health = scene.PlayerHealth[next];
			PlayerWeapon.Show(TraceScene.WeaponAt(scene.PlayerWeapons, tick));
			OpponentWeapon.Show(TraceScene.WeaponAt(scene.OpponentWeapons, tick));
			var last = scene.Damage.LastOrDefault(x => x.ByPlayer && x.Tick <= tick);
			LastHitText = last.Weapon is null
				? "None yet"
				: $"−{last.Damage} · {Display.HitGroup(last.HitGroup)} · {Display.Weapon(last.Weapon)}"
					+ (tick - last.Tick > 2 ? $", {(tick - last.Tick) / (double)trace.TickRate:0.00} s ago" : "");
		}
		else
		{
			// Traces stored before format 3: health only at the start, weapons from the encounter.
			OpponentHealth.Health = e.OpponentHealthAtStart;
			PlayerHealth.Health = e.PlayerHealthAtStart;
			PlayerWeapon.Show(fired.LastOrDefault().Weapon ?? e.FirstShotWeapon);
			OpponentWeapon.Show(e.OpponentWeapon);
			var hits = fired.Where(s => s.Hit).ToList();
			LastHitText = hits.Count == 0 ? "None yet" : $"{Display.HitGroup(hits[^1].HitGroup)}; re-analyze for damage";
		}

		Redraw?.Invoke(this, EventArgs.Empty);
	}

	private void TogglePlay()
	{
		if (IsPlaying)
		{
			Stop();
			return;
		}

		if (Position >= MaxPosition)
			Position = 0;

		_timer ??= CreateTimer();
		_clock.Restart();
		IsPlaying = true;
		_timer.Start();
	}

	private IDispatcherTimer CreateTimer()
	{
		var timer = Application.Current!.Dispatcher.CreateTimer();
		timer.Interval = TimeSpan.FromMilliseconds(16);
		timer.Tick += (_, _) =>
		{
			var elapsed = _clock.Elapsed.TotalSeconds;
			_clock.Restart();
			var next = Position + elapsed * _speed * (_trace?.TickRate ?? 64);
			if (next >= MaxPosition)
			{
				Position = MaxPosition;
				Stop();
				return;
			}

			Position = next;
		};
		return timer;
	}

	private void Step(int ticks)
	{
		Stop();
		Position = Math.Round(Position) + ticks;
	}

	private void SelectSpeed(SpeedOption option)
	{
		foreach (var s in Speeds)
			s.IsSelected = s == option;
		_speed = option.Value;
	}

	private async Task OpenSibling(int? id)
	{
		if (id is { } next)
			await LoadAsync(next);
	}

	private void BuildVerdicts(EncounterRecord e)
	{
		Verdicts.Clear();
		var approach = e.Approach switch
		{
			AimApproach.Overshoot => $"You flicked {e.FlickDistanceDegrees:0.0}° and went {e.PeakOvershootDegrees:0.0}° past the body, then needed {Corrections(e.CorrectionCount)} to come back.",
			AimApproach.Undershoot => $"You stopped short of the body and needed {Corrections(e.CorrectionCount)} to reach it.",
			AimApproach.OnTarget => $"You moved {e.FlickDistanceDegrees:0.0}° onto the body without passing it.",
			_ => "There was too little crosshair movement before the shot to tell.",
		};
		// The chip follows the flags (the classifier, or the user's correction); the text describes the measured path.
		var approachVerdict = e.Has(AimCategory.Overshoot) ? "Overshoot"
			: e.Has(AimCategory.Undershoot) ? "Undershoot"
			: e.Approach == AimApproach.Unknown ? "Unclear"
			: "On target";
		Verdicts.Add(new VerdictItem("Overshoot or undershoot", approachVerdict, e.Has(AimCategory.Overshoot) || e.Has(AimCategory.Undershoot), approach));

		var weapon = Display.Weapon(e.FirstShotWeapon);
		var moving = e.Has(AimCategory.MovingShot);
		var stopping = _accurateSpeed <= 0
			? $"First shot at {e.SpeedAtFirstShot:0} u/s."
			: moving
				? $"First shot at {e.SpeedAtFirstShot:0} u/s, {e.SpeedRatioAtFirstShot:0.00}× the {weapon}'s accurate speed of {_accurateSpeed:0} u/s. {e.ShotsWhileMoving} of {e.ShotCount} shots while moving."
				: $"First shot at {e.SpeedAtFirstShot:0} u/s, under the {weapon}'s accurate speed of {_accurateSpeed:0} u/s.";
		Verdicts.Add(new VerdictItem("Stopped before shooting", moving ? "Moving shot" : "Stopped", moving, stopping));

		var hasty = e.Has(AimCategory.Hasty);
		var slow = e.Has(AimCategory.SlowReaction);
		var ttfs = e.TimeToFirstShot is { } t ? $"{t:0.00} s" : "—";
		var onTarget = e.TimeToOnTarget is { } o ? $" Your crosshair first reached the body at {o:0.00} s." : " Your crosshair never reached the body.";
		var timing = hasty
			? $"You fired {ttfs} after first sighting while {e.AngularErrorAtFirstShot:0.0}° off the body.{onTarget}"
			: slow
				? $"Your first shot came {ttfs} after first sighting.{onTarget}"
				: $"You fired {ttfs} after first sighting.{onTarget}";
		Verdicts.Add(new VerdictItem("Haste or slow reaction", hasty ? "Hasty" : slow ? "Slow reaction" : "On time", hasty || slow, timing + " Sighting is from the spotted flag, so allow ±0.5 s."));
	}

	private void BuildScores(EncounterRecord e)
	{
		Scores.Clear();
		foreach (var c in Enum.GetValues<AimCategory>().OrderByDescending(e.Probability))
		{
			var p = e.Probability(c);
			// The model's own view; corrections are shown on their own card.
			Scores.Add(new ScoreItem(Display.Habit(c), $"{p:P0}", new Rect(0, 0, p, 1), e.ModelFlagged(c) ? Display.Accent : Display.LineStrong, e.ModelFlagged(c)));
		}

		ModelNote = e.ClassifierSource == "rules"
			? "From the rules: no trained model was available, so scores are 0% or 100%."
			: $"{e.ClassifierSource} · flags at 50%.";
	}

	private async Task BuildCorrectionsAsync(EncounterRecord e)
	{
		var verdicts = await _corrections.ForEncounterAsync(e.Id);
		CorrectionItems.Clear();
		foreach (var category in Enum.GetValues<AimCategory>())
			CorrectionItems.Add(new CorrectionItem(category, e.Probability(category), verdicts.TryGetValue(category, out var v) ? v : null, SetCorrectionAsync));
		await Training.RefreshCountsAsync();
	}

	private async Task SetCorrectionAsync(CorrectionItem item, bool? value)
	{
		if (_detail is not { } d)
			return;

		var e = d.Record;
		if (await _corrections.SetAsync(e.Id, item.Category, value) is not { } result)
			return;

		item.UserValue = value;
		e.Flags = result.Flags;
		e.PrimaryCategory = result.Primary;
		BuildVerdicts(e);
		await Training.OnCorrectedAsync();
	}

	private void BuildOutlook(EncounterRecord e)
	{
		Costs.Clear();
		if (_outcomes.Explain(e) is not { } outlook)
			return;

		OutlookTitle = e.Outcome switch
		{
			EncounterOutcome.Death => "What cost the duel",
			EncounterOutcome.Kill => "What made the duel harder",
			_ => "What lowered the first shot's chance",
		};
		HasWinChance = outlook.WinChance is not null;
		WinChance = outlook.WinChance is { } win ? $"{win:P0}" : "—";
		HitChance = outlook.FirstShotHitChance is { } hit ? $"{hit:P0}" : "—";
		foreach (var cost in outlook.Costs)
		{
			Costs.Add(new CostItem(
				cost.Text,
				cost.IsSituation ? "Situation" : "Aim",
				new Rect(0, 0, Math.Max(cost.Weight, 0.04), 1),
				cost.IsSituation ? Display.LineStrong : Display.Loss));
		}

		HasCosts = Costs.Count > 0;
		OutlookNote = (HasCosts
				? "What lowered the predicted chance, largest first. Situation factors are context, not mistakes. "
				: "Nothing in this encounter lowered the predicted chance. ")
			+ $"The model learned what usually decides duels like this one; it can't say what decided this one. Outcome model {outlook.ModelVersion}.";
	}

	private static string Corrections(int count) => count == 1 ? "1 correction" : $"{count} corrections";

}
