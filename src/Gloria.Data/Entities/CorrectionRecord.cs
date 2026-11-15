using Gloria.Core.Classification;

namespace Gloria.Data.Entities;

/// <summary>
/// The user's verdict on one aim category of one encounter. It overrides the classifier in what the app shows,
/// and counts extra when the user's model is retrained.
/// </summary>
/// <remarks>
/// Keyed by what identifies the encounter in the demo (demo hash, player, opponent, first-shot tick) rather than by
/// encounter id: re-analysis deletes and re-creates encounters, and corrections must survive it. There is no foreign
/// key on purpose. Round numbers are not part of the key: they shift when round parsing changes (parser version 5
/// added the first round).
/// </remarks>
public sealed class CorrectionRecord
{
    public int Id { get; set; }

    public required string DemoHash { get; set; }

    public ulong PlayerSteamId { get; set; }

    public ulong OpponentSteamId { get; set; }

    /// <summary>The round when the verdict was last set, for reading only; not part of the key.</summary>
    public int RoundNumber { get; set; }

    public int FirstShotTick { get; set; }

    public AimCategory Category { get; set; }

    /// <summary>Whether the category applies, in the user's judgment.</summary>
    public bool Value { get; set; }

    /// <summary>When the verdict was last set.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
