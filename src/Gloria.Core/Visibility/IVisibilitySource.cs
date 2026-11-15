using Gloria.Core.Ingestion;

namespace Gloria.Core.Visibility;

/// <summary>
/// Decides whether one player can see another at a tick. Swappable because the game's
/// "spotted" state is not true line of sight; visibility-based timings are approximations.
/// </summary>
public interface IVisibilitySource
{
    bool CanSee(ParsedDemo demo, int tick, ulong observerSteamId, ulong targetSteamId);
}
