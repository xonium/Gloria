namespace Gloria.Core.Classification;

/// <summary>
/// Aim faults a frag or death can be tagged with. An encounter can have several;
/// one with none is "clean".
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
}
