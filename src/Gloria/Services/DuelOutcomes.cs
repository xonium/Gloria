using Gloria.Core.Encounters;
using Gloria.Data.Entities;
using Gloria.Ml;

namespace Gloria.Services;

/// <summary>One factor that lowered a predicted outcome, in words.</summary>
/// <param name="Weight">Share of the largest factor's contribution, 0..1, for drawing a bar.</param>
/// <param name="IsSituation">Context the player didn't aim badly at (HP, weapons, distance, who fired first), not a mistake.</param>
public sealed record DuelCost(string Text, float Weight, bool IsSituation);

/// <summary>How often one factor was what cost the most in the duels a player lost.</summary>
/// <param name="Largest">Lost duels where this was the largest factor of all.</param>
/// <param name="LargestAim">Lost duels where this was the largest aim factor; 0 for situation factors.</param>
public sealed record LossCause(string Name, bool IsSituation, int Largest, int LargestAim);

/// <param name="Duels">Lost duels explained.</param>
/// <param name="WithAimCost">Lost duels where at least one aim factor lowered the chance.</param>
public sealed record LossCauses(int Duels, int WithAimCost, IReadOnlyList<LossCause> Causes);

/// <param name="WinChance">Null when the encounter ended without a kill or death, so there was no duel to win.</param>
public sealed record DuelOutlook(float? WinChance, float? FirstShotHitChance, IReadOnlyList<DuelCost> Costs, string ModelVersion);

/// <summary>
/// Runs the outcome models on a stored encounter ("what cost the duel"). The models were trained on the
/// demos, so the costs say what usually decides duels like this one, not what decided this one for certain.
/// </summary>
public sealed class DuelOutcomes(MlOutcomePredictor? predictor)
{
	private const int MaxCosts = 4;

	private static readonly HashSet<string> Situation =
	[
		nameof(MlEncounterRow.DistanceToEnemy),
		nameof(MlEncounterRow.WeaponClass),
		nameof(MlEncounterRow.PlayerHealth),
		nameof(MlEncounterRow.OpponentHealth),
		nameof(MlEncounterRow.OpponentWeaponClass),
		nameof(MlEncounterRow.FiredFirst),
	];

	public bool IsAvailable => predictor is not null;

	public DuelOutlook? Explain(EncounterRecord e)
	{
		if (predictor is null)
			return null;

		var predictions = predictor.Predict([e.ToFeatures()])[0];
		var won = predictions.FirstOrDefault(p => p.Target == OutcomeTarget.WonDuel);
		var hit = predictions.FirstOrDefault(p => p.Target == OutcomeTarget.FirstShotHit);
		var isDuel = e.Outcome is EncounterOutcome.Kill or EncounterOutcome.Death;

		// Explain the duel when there was one, else the first shot.
		var explained = isDuel ? won ?? hit : hit;
		var costs = explained is null ? [] : Costs(e, explained);
		return new DuelOutlook(isDuel ? won?.Probability : null, hit?.Probability, costs, predictor.Manifest.ModelVersion);
	}

	/// <summary>
	/// Runs the duel model on lost duels and counts, per factor, how often it cost the most. Null when there is no
	/// outcome model. CPU-bound: call it off the UI thread.
	/// </summary>
	public LossCauses? ExplainLosses(IReadOnlyList<EncounterRecord> losses)
	{
		if (predictor is null)
			return null;

		var largest = new Dictionary<string, int>();
		var largestAim = new Dictionary<string, int>();
		var withAim = 0;
		var predictions = predictor.Predict(losses.Select(e => e.ToFeatures()).ToList());
		foreach (var prediction in predictions)
		{
			if (prediction.FirstOrDefault(p => p.Target == OutcomeTarget.WonDuel) is not { } won)
				continue;

			var costs = Merge(won);
			if (costs.Count == 0)
				continue;

			Increment(largest, costs[0].Feature);
			if (costs.FirstOrDefault(c => !Situation.Contains(c.Feature)) is { Feature: not null } aim)
			{
				Increment(largestAim, aim.Feature);
				withAim++;
			}
		}

		var causes = largest.Keys.Union(largestAim.Keys)
			.Select(f => new LossCause(Name(f), Situation.Contains(f), largest.GetValueOrDefault(f), largestAim.GetValueOrDefault(f)))
			.OrderByDescending(c => c.Largest)
			.ToList();
		return new LossCauses(losses.Count, withAim, causes);
	}

	/// <summary>A factor in general terms, for summaries over many duels.</summary>
	public static string Name(string feature) => feature switch
	{
		nameof(MlEncounterRow.TimeToFirstShot) => "Time to the first shot",
		nameof(MlEncounterRow.TimeToOnTarget) => "Time to get the crosshair on the body",
		nameof(MlEncounterRow.AngularErrorAtFirstVisible) => "Crosshair placement when they appeared",
		nameof(MlEncounterRow.AngularErrorAtFirstShot) => "Crosshair off the body at the first shot",
		nameof(MlEncounterRow.PeakOvershootDegrees) => "Overshooting the body",
		nameof(MlEncounterRow.CorrectionCount) => "Small corrections before the shot",
		nameof(MlEncounterRow.FlickDistanceDegrees) => "Size of the flick",
		nameof(MlEncounterRow.SpeedRatioAtFirstShot) => "Moving at the first shot",
		nameof(MlEncounterRow.ShotsWhileMovingRatio) => "Shooting while moving",
		nameof(MlEncounterRow.DistanceToEnemy) => "Distance",
		nameof(MlEncounterRow.WeaponClass) => "Your weapon",
		nameof(MlEncounterRow.PlayerHealth) => "Your HP",
		nameof(MlEncounterRow.OpponentHealth) => "Their HP",
		nameof(MlEncounterRow.OpponentWeaponClass) => "Their weapon",
		nameof(MlEncounterRow.FiredFirst) => "Who fired first",
		_ => feature,
	};

	private static void Increment(Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;

	/// <summary>The factors that lowered the prediction, largest first.</summary>
	private static List<(string Feature, float Contribution)> Merge(OutcomePrediction prediction) => prediction.Costs
		// TimeToOnTarget and ReachedTarget describe the same thing; count them as one factor.
		.GroupBy(c => c.Feature == nameof(MlEncounterRow.ReachedTarget) ? nameof(MlEncounterRow.TimeToOnTarget) : c.Feature)
		.Select(g => (Feature: g.Key, Contribution: g.Sum(c => c.Contribution)))
		.OrderBy(c => c.Contribution)
		.ToList();

	private static List<DuelCost> Costs(EncounterRecord e, OutcomePrediction prediction)
	{
		var merged = Merge(prediction).Take(MaxCosts).ToList();
		if (merged.Count == 0)
			return [];

		var largest = Math.Abs(merged[0].Contribution);
		return merged
			.Select(c => new DuelCost(Describe(e, c.Feature), largest > 0 ? Math.Abs(c.Contribution) / largest : 0, Situation.Contains(c.Feature)))
			.ToList();
	}

	private static string Describe(EncounterRecord e, string feature) => feature switch
	{
		nameof(MlEncounterRow.TimeToFirstShot) => $"First shot {e.TimeToFirstShot ?? 0:0.00} s after sighting",
		nameof(MlEncounterRow.TimeToOnTarget) => e.TimeToOnTarget is { } t
			? $"Crosshair reached the body {t:0.00} s after sighting"
			: "Crosshair never reached the body",
		nameof(MlEncounterRow.AngularErrorAtFirstVisible) => $"Crosshair {e.AngularErrorAtStart:0.0}° off the body when they were spotted",
		nameof(MlEncounterRow.AngularErrorAtFirstShot) => e.AngularErrorAtFirstShot < 0.05f
			? "Crosshair on the body at the first shot"
			: $"Crosshair {e.AngularErrorAtFirstShot:0.0}° off the body at the first shot",
		nameof(MlEncounterRow.PeakOvershootDegrees) => $"Went {e.PeakOvershootDegrees:0.0}° past the body",
		nameof(MlEncounterRow.CorrectionCount) => e.CorrectionCount == 1 ? "1 correction before the shot" : $"{e.CorrectionCount} corrections before the shot",
		nameof(MlEncounterRow.FlickDistanceDegrees) => $"A {e.FlickDistanceDegrees:0.0}° flick to the target",
		nameof(MlEncounterRow.SpeedRatioAtFirstShot) => $"First shot at {e.SpeedRatioAtFirstShot:0.00}× the accurate speed",
		nameof(MlEncounterRow.ShotsWhileMovingRatio) => $"{e.ShotsWhileMoving} of {e.ShotCount} shots while moving",
		nameof(MlEncounterRow.DistanceToEnemy) => $"{e.DistanceAtFirstShot:N0} units apart",
		nameof(MlEncounterRow.WeaponClass) => $"You had the {Display.Weapon(e.FirstShotWeapon)}",
		nameof(MlEncounterRow.PlayerHealth) => $"You had {e.PlayerHealthAtStart} HP",
		nameof(MlEncounterRow.OpponentHealth) => $"They had {e.OpponentHealthAtStart} HP",
		nameof(MlEncounterRow.OpponentWeaponClass) => $"They had the {Display.Weapon(e.OpponentWeapon)}",
		nameof(MlEncounterRow.FiredFirst) => e.FiredFirst ? "You fired first" : "They fired first",
		_ => feature,
	};
}
