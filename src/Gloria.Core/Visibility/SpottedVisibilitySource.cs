using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;

namespace Gloria.Core.Visibility;

/// <summary>Visibility from the game's spotted state. It lags true line of sight by a few ticks.</summary>
public sealed class SpottedVisibilitySource : IVisibilitySource
{
    public static SpottedVisibilitySource Instance { get; } = new();

    public bool CanSee(ParsedDemo demo, int tick, ulong observerSteamId, ulong targetSteamId) =>
        demo.Timeline.IsSpottedBy(targetSteamId, observerSteamId, tick);
}

/// <summary>
/// Fallback for demos without spotted state: the target is alive, in range and inside the
/// observer's view cone. It ignores walls and smokes, so it overestimates visibility.
/// </summary>
public sealed class FieldOfViewVisibilitySource(float halfAngleDegrees = 53f, float maxDistance = 4000f) : IVisibilitySource
{
    public bool CanSee(ParsedDemo demo, int tick, ulong observerSteamId, ulong targetSteamId)
    {
        if (!demo.Timeline.TryGet(observerSteamId, tick, out var observer) || !observer.IsAlive
            || !demo.Timeline.TryGet(targetSteamId, tick, out var target) || !target.IsAlive)
        {
            return false;
        }

        return BodyModel.Distance(observer, target) <= maxDistance
               && BodyModel.AngularError(observer, target) <= halfAngleDegrees;
    }
}

public static class VisibilitySources
{
    /// <summary>The spotted state when the demo has it, otherwise the view-cone fallback.</summary>
    public static IVisibilitySource For(ParsedDemo demo) =>
        demo.Timeline.HasSpottedData ? SpottedVisibilitySource.Instance : new FieldOfViewVisibilitySource();
}
