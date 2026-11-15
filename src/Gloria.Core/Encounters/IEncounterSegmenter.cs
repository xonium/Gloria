using Gloria.Core.Ingestion;

namespace Gloria.Core.Encounters;

/// <summary>Turns a parsed tick stream into encounters for one player.</summary>
public interface IEncounterSegmenter
{
    IReadOnlyList<Encounter> Segment(ParsedDemo demo, ulong playerSteamId);
}
