using Gloria.Core.Ingestion;

namespace Gloria.Core.Encounters;

/// <summary>Turns a parsed tick stream into encounters for one player.</summary>
public interface IEncounterSegmenter
{
    /// <summary>Bumped when segmentation changes, so cached encounters can be recomputed.</summary>
    int Version { get; }

    IReadOnlyList<Encounter> Segment(ParsedDemo demo, ulong playerSteamId);
}
