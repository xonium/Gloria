using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Gloria.Core.Analysis;
using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Metrics;
using Gloria.Core.Weapons;
using Gloria.Data.Entities;
using Gloria.Data.Queries;
using Gloria.Graphics;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed record VerdictItem(string Question, string Verdict, bool Flagged, string Text)
{
	public bool NotFlagged => !Flagged;
}

public sealed record ScoreItem(string Name, string Percent, Rect Bar, Color Color, bool Flagged);

public sealed record ShotRow(string Number, string At, string Result, string Offset, string Speed, string Moving);

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
	private string _markers = "";
	private string _modelNote = "";
	private bool _hasTrace;

	public EncounterViewModel(GloriaReadModel readModel)
	{
		_readModel = readModel;
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

	public TraceChartDrawable ErrorChart { get; } = new() { FormatY = v => $"{v:0.#}°" };

	public TraceChartDrawable SpeedChart { get; } = new() { MinY = 0, MaxY = 260 };

	public IReadOnlyList<SpeedOption> Speeds { get; }

	public ObservableCollection<VerdictItem> Verdicts { get; } = [];

	public ObservableCollection<ScoreItem> Scores { get; } = [];

	public ObservableCollection<ShotRow> Shots { get; } = [];

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

	public string Markers { get => _markers; private set => Set(ref _markers, value); }

	public string ModelNote { get => _modelNote; private set => Set(ref _modelNote, value); }

	public bool HasTrace { get => _hasTrace; private set => Set(ref _hasTrace, value); }

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
		LoadTrace(d);
	}

	private void LoadTrace(EncounterDetail d)
	{
		_trace = d.Trace;
		HasTrace = _trace is { Count: > 1 };
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
				shot.Hit ? $"Hit · {HitGroup(shot.HitGroup)}" : "Missed",
				Math.Abs(offset) < 0.05f ? "on" : $"{Math.Abs(offset):0.0}°{(offset < 0 ? " past" : "")}",
				$"{speed:0} u/s",
				_accurateSpeed > 0 && speed > _accurateSpeed ? $"Yes, {speed / _accurateSpeed:0.00}×" : "No"));
		}

		Markers = string.Join("   ·   ", new[] { "Seen 0.00 s" }
			.Concat(trace.Shots.Select((s, k) => $"Shot {k + 1} {trace.SecondsAt(trace.IndexOf(s.Tick), e.StartTick):0.00} s{(s.Hit ? "" : " (miss)")}"))
			.Append($"{Display.Outcome(e.Outcome)} {trace.SecondsAt(trace.Count - 1, e.StartTick):0.00} s"));

		MaxPosition = trace.Count - 1;
		// Start on the first shot, where most of the story is visible.
		_position = -1;
		Position = trace.Shots.Count > 0 ? trace.IndexOf(trace.Shots[0].Tick - 1) : _startIndex;
	}

	private void UpdateFrame()
	{
		if (_trace is not { } trace || _detail is not { } d)
			return;

		var i = Math.Clamp((int)Position, 0, trace.Count - 1);
		Replay.Position = (float)Position;
		ErrorChart.Playhead = (float)Position;
		SpeedChart.Playhead = (float)Position;

		var e = d.Record;
		var t = trace.SecondsAt(i, e.StartTick);
		TimeText = $"{(t < 0 ? "−" : "")}{Math.Abs(t):0.00} s";
		// Same sample the replay draws a shot at: the tick before it fired.
		var fired = trace.Shots.Where(s => trace.IndexOf(s.Tick - 1) <= i).ToList();
		var error = trace.BodyError[i];
		PhaseText = i < _startIndex ? "Not spotted yet; may already be in view"
			: i >= trace.Count - 1 ? Display.Outcome(e.Outcome)
			: fired.Count > 0 ? $"Shooting: {fired.Count} fired, {fired.Count(s => s.Hit)} hit"
			: error <= 0.05f && error >= -0.05f ? "Crosshair on the body"
			: error < 0 ? "Past the body: overshoot"
			: "Moving toward the target";
		CrosshairText = Math.Abs(error) < 0.05f ? "On the body" : $"{Math.Abs(error):0.0}° {(error < 0 ? "past the body" : "off the body")}";
		SpeedText = _accurateSpeed > 0
			? $"{trace.Speed[i]:0} u/s · {trace.Speed[i] / _accurateSpeed:0.00}× accurate"
			: $"{trace.Speed[i]:0} u/s";
		ShotsText = $"{fired.Count} of {trace.Shots.Count} · {fired.Count(s => s.Hit)} hit";
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
		Verdicts.Add(new VerdictItem("Overshoot or undershoot", e.Approach == AimApproach.OnTarget ? "On target" : e.Approach.ToString(), e.Approach is AimApproach.Overshoot or AimApproach.Undershoot, approach));

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
			Scores.Add(new ScoreItem(Display.Habit(c), $"{p:P0}", new Rect(0, 0, p, 1), e.Has(c) ? Display.Accent : Display.LineStrong, e.Has(c)));
		}

		ModelNote = e.ClassifierSource == "rules"
			? "From the rules: no trained model was available, so scores are 0% or 100%."
			: $"{e.ClassifierSource} · flags at 50%.";
	}

	private static string Corrections(int count) => count == 1 ? "1 correction" : $"{count} corrections";

	private static string HitGroup(int? group) => group switch
	{
		1 => "head",
		2 => "chest",
		3 => "stomach",
		4 or 5 => "arm",
		6 or 7 => "leg",
		_ => "body",
	};
}
