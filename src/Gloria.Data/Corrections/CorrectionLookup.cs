using Gloria.Core.Classification;
using Gloria.Data.Entities;

namespace Gloria.Data.Corrections;

/// <summary>The corrections of one demo, by the encounter they belong to.</summary>
public sealed class CorrectionLookup
{
    private readonly Dictionary<(ulong Player, ulong Opponent, int FirstShotTick), Dictionary<AimCategory, bool>> _byEncounter = [];

    public CorrectionLookup(IEnumerable<CorrectionRecord> corrections)
    {
        foreach (var c in corrections)
        {
            var key = (c.PlayerSteamId, c.OpponentSteamId, c.FirstShotTick);
            if (!_byEncounter.TryGetValue(key, out var categories))
                _byEncounter[key] = categories = [];
            categories[c.Category] = c.Value;
        }
    }

    public static CorrectionLookup Empty { get; } = new([]);

    /// <summary>The user's verdicts for the encounter, or null if there are none.</summary>
    public IReadOnlyDictionary<AimCategory, bool>? For(EncounterRecord e) =>
        _byEncounter.GetValueOrDefault((e.PlayerSteamId, e.OpponentSteamId, e.FirstShotTick));
}
