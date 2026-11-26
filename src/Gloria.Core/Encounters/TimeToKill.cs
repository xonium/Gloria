namespace Gloria.Core.Encounters;

/// <summary>
/// Time to kill: from the player's first shot at the opponent, hit or miss, to the opponent's death by the player.
/// On a <see cref="EncounterOutcome.Kill"/> the encounter ends on the kill tick, so it is the time from the first shot
/// to the end. A kill finished with a grenade or a knife still counts, since the encounter only needs one gun shot.
/// </summary>
public static class TimeToKill
{
    /// <summary>An opponent's health at the start of a duel for its kill to count; chip damage from others would flatter it.</summary>
    public const int FullHealth = 100;

    /// <summary>Seconds from the first shot to the kill; null unless the player won by killing the opponent.</summary>
    public static double? Seconds(EncounterOutcome outcome, int firstShotTick, int endTick, int tickRate) =>
        outcome == EncounterOutcome.Kill && endTick >= firstShotTick && tickRate > 0
            ? (endTick - firstShotTick) / (double)tickRate
            : null;

    /// <summary>Whether a kill counts toward the player's time to kill: the opponent was at full health when the duel began.</summary>
    public static bool Counts(int opponentHealthAtStart) => opponentHealthAtStart >= FullHealth;
}
