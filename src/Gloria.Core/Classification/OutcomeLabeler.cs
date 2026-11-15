using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;

namespace Gloria.Core.Classification;

/// <summary>
/// Objective training labels read from what happened in the demo. They are targets to predict,
/// never inputs: <see cref="EncounterFeatures"/> must not contain them.
/// </summary>
/// <param name="FirstShotHit">Did the first shot damage the opponent? Null when another shot could have dealt that damage.</param>
/// <param name="WonDuel">True for a kill, false for a death, null for a disengage or timeout.</param>
public sealed record OutcomeLabels(bool? FirstShotHit, bool? WonDuel);

public sealed class OutcomeLabeler
{
    public int Version => 1;

    public OutcomeLabels Label(ParsedDemo demo, Encounter encounter, EncounterMetrics metrics)
    {
        var shots = DemoIndex.For(demo).ShotsBy(encounter.PlayerSteamId)
            .Where(s => s.Target == encounter.OpponentSteamId && s.Tick >= metrics.FirstShotTick && s.Tick <= encounter.EndTick)
            .ToList();

        bool? firstShotHit = null;
        if (shots.Count > 0)
        {
            var first = shots[0];
            var next = shots.Count > 1 ? shots[1].Tick : int.MaxValue;
            firstShotHit = first.HitTick switch
            {
                null => false,
                { } hit when next <= hit => null,
                _ => true,
            };
        }

        bool? won = encounter.Outcome switch
        {
            EncounterOutcome.Kill => true,
            EncounterOutcome.Death => false,
            _ => null,
        };

        return new OutcomeLabels(firstShotHit, won);
    }
}
