namespace Gloria.Core.Encounters;

public enum EncounterOutcome
{
    Kill,
    Death,
    Disengage,
    Timeout,
}

/// <summary>An engagement between the analyzed player and one opponent within a round.</summary>
public sealed record Encounter(
    int RoundNumber,
    ulong PlayerSteamId,
    ulong OpponentSteamId,
    int StartTick,
    int EndTick,
    EncounterOutcome Outcome);
