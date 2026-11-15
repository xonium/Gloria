using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;

namespace Gloria.Core.Metrics;

public enum AimApproach
{
    Unknown,
    OnTarget,
    Overshoot,
    Undershoot,
}

/// <summary>Per-encounter aim scores. Times are in seconds; null when the event never happened.</summary>
public sealed record EncounterMetrics(
    Encounter Encounter,
    AimApproach Approach,
    double? TimeToFirstShot,
    double? TimeToOnTarget,
    IReadOnlyList<float> SpeedAtShots,
    int ShotsWhileMoving);

public interface IAimMetricsCalculator
{
    /// <summary>Bumped when metric logic changes, so stored metrics can be recomputed.</summary>
    int Version { get; }

    EncounterMetrics Calculate(ParsedDemo demo, Encounter encounter);
}
