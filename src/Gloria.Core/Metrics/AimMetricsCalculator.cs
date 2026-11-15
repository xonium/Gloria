using System.Numerics;
using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Weapons;

namespace Gloria.Core.Metrics;

public sealed record AimMetricsOptions
{
    /// <summary>The crosshair is on target when its error to the body axis is at most this many body half-widths.</summary>
    public float OnTargetBodyHalfWidths { get; init; } = 1f;

    /// <summary>Below this initial error there is no approach to overshoot or undershoot.</summary>
    public float MinApproachDegrees { get; init; } = 3f;

    /// <summary>
    /// How far back from the first shot to look for the start of the view movement that led to it.
    /// The approach window does not start at first visibility, because the spotted state lags by up to half a second.
    /// </summary>
    public float MaxApproachSeconds { get; init; } = 1f;

    /// <summary>A movement chain ends when the view has been still for this many ticks (about 150 ms); shorter pauses separate corrections.</summary>
    public int ApproachRestTicks { get; init; } = 10;

    /// <summary>An overshoot counts only if the crosshair then comes back by this fraction of the peak.</summary>
    public float OvershootReturnFraction { get; init; } = 0.5f;

    /// <summary>How long after the first shot the crosshair may still come back from an overshoot.</summary>
    public float OvershootReturnSeconds { get; init; } = 0.5f;

    /// <summary>A view movement starts above this angular speed (degrees per second)...</summary>
    public float MovementStartDegreesPerSecond { get; init; } = 20f;

    /// <summary>...and ends below this one.</summary>
    public float MovementEndDegreesPerSecond { get; init; } = 8f;

    /// <summary>
    /// Where between the previous tick (0) and the fire tick (1) a shot is evaluated. CS2 uses sub-tick
    /// input, so a shot recorded at tick t happened somewhere in (t−1, t], and the view angles recorded
    /// at t already include movement after the shot. On the real demos, the error at t−1 predicts hits
    /// best (see the trainer report's sub-tick alignment section).
    /// </summary>
    public float ShotSubTickFraction { get; init; } = 0f;

    /// <summary>How far before the encounter start an opponent's shot at the player still counts for who fired first.</summary>
    public float OpponentShotLookbackSeconds { get; init; } = 1f;
}

/// <summary>Scores one encounter from the analyzed player's view angles and movement relative to the opponent.</summary>
public sealed class AimMetricsCalculator(IWeaponData? weapons = null, AimMetricsOptions? options = null) : IAimMetricsCalculator
{
    // Used for guns missing from the weapon table: a typical 240 u/s max speed.
    private const float FallbackAccurateSpeed = 240f * 0.34f;

    private readonly IWeaponData _weapons = weapons ?? StaticWeaponData.Instance;
    private readonly AimMetricsOptions _options = options ?? new AimMetricsOptions();

    public int Version => 5;

    public EncounterMetrics? Calculate(ParsedDemo demo, Encounter encounter)
    {
        var timeline = demo.Timeline;
        var player = encounter.PlayerSteamId;
        var opponent = encounter.OpponentSteamId;
        var shots = DemoIndex.For(demo).ShotsBy(player)
            .Where(s => s.Target == opponent && s.Tick >= encounter.StartTick && s.Tick <= encounter.EndTick)
            .ToList();
        if (shots.Count == 0
            || !timeline.TryGet(player, encounter.StartTick, out var meAtStart)
            || !timeline.TryGet(opponent, encounter.StartTick, out var themAtStart))
        {
            return null;
        }

        var first = shots[0];
        if (!TryGetAtShot(timeline, player, opponent, first.Tick, out var me, out var them))
            return null;

        float ErrorAt(int tick) =>
            timeline.TryGet(player, tick, out var p) && timeline.TryGet(opponent, tick, out var o) ? BodyModel.AngularError(p, o) : float.NaN;

        var speeds = new List<float>(shots.Count);
        var moving = 0;
        foreach (var shot in shots)
        {
            if (!TryGetAtShot(timeline, player, opponent, shot.Tick, out var shooter, out _))
                continue;

            var speed = shooter.HorizontalSpeed;
            speeds.Add(speed);
            if (speed > AccurateSpeed(shot.Weapon, shooter.IsScoped))
                moving++;
        }

        var rate = demo.TickRate;
        var errorAtStart = BodyModel.AngularError(meAtStart, themAtStart);
        var timeToOnTarget = TimeToOnTarget(timeline, encounter, rate);
        var approachStart = ApproachStartTick(timeline, player, first.Tick, rate);
        timeline.TryGet(player, approachStart, out var meAtApproach);
        var (peakOvershoot, corrections) = Approach(timeline, encounter, approachStart, first.Tick, rate);
        var approach = peakOvershoot >= 1.5f ? AimApproach.Overshoot
            : corrections >= 2 ? AimApproach.Undershoot
            : timeToOnTarget is not null ? AimApproach.OnTarget
            : AimApproach.Unknown;

        return new EncounterMetrics(
            encounter,
            approach,
            TimeToFirstShot: (first.Tick - encounter.StartTick) / (double)rate,
            timeToOnTarget,
            speeds,
            moving,
            first.Tick,
            first.Weapon,
            [.. shots.Select(s => s.Tick)],
            errorAtStart,
            BodyModel.AngularError(me, them),
            peakOvershoot,
            corrections,
            FlickDistanceDegrees: AimMath.AngleBetween(meAtApproach.Yaw, meAtApproach.Pitch, me.Yaw, me.Pitch),
            me.HorizontalSpeed / AccurateSpeed(first.Weapon, me.IsScoped),
            BodyModel.Distance(me, them),
            ErrorAt(first.Tick),
            ErrorAt(first.Tick - 1),
            ErrorAt(first.Tick - 2),
            approachStart,
            HealthBefore(timeline, player, encounter.StartTick),
            HealthBefore(timeline, opponent, encounter.StartTick),
            timeline.TryGet(opponent, first.Tick - 1, out var themBeforeShot) ? themBeforeShot.ActiveWeapon : them.ActiveWeapon,
            OpponentFirstShotTick(demo, encounter));
    }

    /// <summary>
    /// HP one tick before <paramref name="tick"/>. Context is read before the encounter and the first shot:
    /// the state recorded on a fire tick already includes that shot's damage, and a killed opponent holds no weapon.
    /// </summary>
    private static int HealthBefore(PlayerTimeline timeline, ulong steamId, int tick) =>
        timeline.TryGet(steamId, tick - 1, out var before) ? before.Health
        : timeline.TryGet(steamId, tick, out var at) ? at.Health
        : 100;

    /// <summary>
    /// The opponent's first shot at the player from shortly before the encounter (missed shots are not
    /// contact, so the encounter can start after them) until its end.
    /// </summary>
    private int? OpponentFirstShotTick(ParsedDemo demo, Encounter encounter)
    {
        var from = encounter.StartTick - (int)(_options.OpponentShotLookbackSeconds * demo.TickRate);
        foreach (var shot in DemoIndex.For(demo).ShotsBy(encounter.OpponentSteamId))
        {
            if (shot.Target == encounter.PlayerSteamId && shot.Tick >= from && shot.Tick <= encounter.EndTick)
                return shot.Tick;
        }

        return null;
    }

    /// <summary>
    /// Start of the chain of view movements that ends at the first shot: scanning back from the shot,
    /// the first point where the view had been still for <see cref="AimMetricsOptions.ApproachRestTicks"/>.
    /// Equals the shot tick when the crosshair was already resting (pre-aimed).
    /// </summary>
    private int ApproachStartTick(PlayerTimeline timeline, ulong player, int shotTick, int rate)
    {
        var onset = shotTick;
        var rest = 0;
        var earliest = shotTick - (int)(_options.MaxApproachSeconds * rate);
        for (var tick = shotTick; tick > earliest; tick--)
        {
            if (!timeline.TryGet(player, tick, out var now) || !timeline.TryGet(player, tick - 1, out var before))
                break;

            var speed = AimMath.AngleBetween(before.Yaw, before.Pitch, now.Yaw, now.Pitch) * rate;
            if (speed < _options.MovementEndDegreesPerSecond)
            {
                if (++rest >= _options.ApproachRestTicks)
                    break;
            }
            else
            {
                rest = 0;
                onset = tick - 1;
            }
        }

        return onset;
    }

    private float OnTargetDegrees(in PlayerTickState me, in PlayerTickState them) =>
        _options.OnTargetBodyHalfWidths * BodyModel.HalfWidthDegrees(BodyModel.Distance(me, them));

    private float AccurateSpeed(string weapon, bool scoped) =>
        _weapons.Find(weapon)?.AccurateSpeedThresholdWhen(scoped) ?? FallbackAccurateSpeed;

    /// <summary>Both players' state at the sub-tick moment of a shot fired at <paramref name="tick"/>.</summary>
    private bool TryGetAtShot(PlayerTimeline timeline, ulong player, ulong opponent, int tick, out PlayerTickState me, out PlayerTickState them)
    {
        if (!timeline.TryGet(player, tick, out me) || !timeline.TryGet(opponent, tick, out them))
        {
            them = default;
            return false;
        }

        if (timeline.TryGet(player, tick - 1, out var mePrev) && timeline.TryGet(opponent, tick - 1, out var themPrev))
        {
            me = BodyModel.Lerp(mePrev, me, _options.ShotSubTickFraction);
            them = BodyModel.Lerp(themPrev, them, _options.ShotSubTickFraction);
        }

        return true;
    }

    private double? TimeToOnTarget(PlayerTimeline timeline, Encounter encounter, int rate)
    {
        for (var tick = encounter.StartTick; tick <= encounter.EndTick; tick++)
        {
            if (timeline.TryGet(encounter.PlayerSteamId, tick, out var me)
                && timeline.TryGet(encounter.OpponentSteamId, tick, out var them)
                && BodyModel.AngularError(me, them) <= OnTargetDegrees(me, them))
            {
                return (tick - encounter.StartTick) / (double)rate;
            }
        }

        return null;
    }

    /// <summary>Peak overshoot and correction count in the approach window [approach start, first shot].</summary>
    private (float PeakOvershoot, int Corrections) Approach(PlayerTimeline timeline, Encounter encounter, int approachStart, int firstShotTick, int rate)
    {
        var player = encounter.PlayerSteamId;
        var opponent = encounter.OpponentSteamId;
        var returnEnd = firstShotTick + (int)(_options.OvershootReturnSeconds * rate);

        var signed = new List<Vector2>();
        var errors = new List<float>();
        var tolerances = new List<float>();
        var speeds = new List<float>();
        PlayerTickState? previous = null;
        var shotIndex = 0;
        for (var tick = approachStart; tick <= returnEnd; tick++)
        {
            if (!timeline.TryGet(player, tick, out var me) || !timeline.TryGet(opponent, tick, out var them))
                continue;
            if (tick > firstShotTick && (!me.IsAlive || !them.IsAlive))
                break;

            signed.Add(BodyModel.SignedError(me, them));
            errors.Add(BodyModel.AngularError(me, them));
            tolerances.Add(OnTargetDegrees(me, them));
            speeds.Add(previous is { } p ? AimMath.AngleBetween(p.Yaw, p.Pitch, me.Yaw, me.Pitch) * rate : 0f);
            previous = me;
            if (tick <= firstShotTick)
                shotIndex = signed.Count - 1;
        }

        if (signed.Count == 0)
            return (0f, 0);

        return (PeakOvershoot(signed, shotIndex), Corrections(errors, tolerances, speeds, shotIndex));
    }

    /// <summary>
    /// How far the crosshair went past the target along the initial approach direction, counted only if
    /// it then came back by <see cref="AimMetricsOptions.OvershootReturnFraction"/> of the peak.
    /// </summary>
    private float PeakOvershoot(List<Vector2> signed, int shotIndex)
    {
        var initial = signed[0];
        if (initial.Length() < _options.MinApproachDegrees)
            return 0f;

        var axis = Vector2.Normalize(initial);
        var minAlong = 0f;
        var minIndex = -1;
        for (var i = 0; i <= shotIndex; i++)
        {
            var along = Vector2.Dot(signed[i], axis);
            if (along < minAlong)
            {
                minAlong = along;
                minIndex = i;
            }
        }

        if (minIndex < 0)
            return 0f;

        var peak = -minAlong;
        for (var i = minIndex + 1; i < signed.Count; i++)
        {
            if (Vector2.Dot(signed[i], axis) >= -peak * (1 - _options.OvershootReturnFraction))
                return peak;
        }

        return 0f;
    }

    /// <summary>
    /// View sub-movements before the first shot that reduced the error while it was still above the
    /// on-target tolerance, minus the initial flick.
    /// </summary>
    private int Corrections(List<float> errors, List<float> tolerances, List<float> speeds, int shotIndex)
    {
        var movements = 0;
        var moving = false;
        var startError = 0f;
        var startTolerance = 0f;
        for (var i = 0; i <= shotIndex; i++)
        {
            if (!moving && speeds[i] > _options.MovementStartDegreesPerSecond)
            {
                moving = true;
                startError = errors[Math.Max(0, i - 1)];
                startTolerance = tolerances[Math.Max(0, i - 1)];
            }
            else if (moving && (speeds[i] < _options.MovementEndDegreesPerSecond || i == shotIndex))
            {
                moving = false;
                if (startError > startTolerance && errors[i] < startError)
                    movements++;
            }
        }

        return Math.Max(0, movements - 1);
    }
}
