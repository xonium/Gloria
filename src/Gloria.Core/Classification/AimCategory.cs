namespace Gloria.Core.Classification;

/// <summary>
/// Aim faults a frag or death can be tagged with. An encounter can have several;
/// one with none is "clean". The values are stored (as bit positions and in corrections), so new ones go at the end.
/// </summary>
public enum AimCategory
{
    /// <summary>The crosshair went past the target and had to come back.</summary>
    Overshoot,

    /// <summary>The crosshair stopped short and needed several small corrections.</summary>
    Undershoot,

    /// <summary>The first shot was fired faster than the weapon's accurate speed (no counter-strafe).</summary>
    MovingShot,

    /// <summary>The first shot was fired before the crosshair was on target.</summary>
    Hasty,

    /// <summary>The first shot came late after the enemy became visible.</summary>
    SlowReaction,

    /// <summary>The crosshair started 5–15° off the enemy: the pre-aim was a little off.</summary>
    SmallFlick,

    /// <summary>The crosshair started 15–45° off the enemy: poor pre-aim.</summary>
    MediumFlick,

    /// <summary>The crosshair started 45° or more off the enemy: no pre-aim at all.</summary>
    LargeFlick,
}
