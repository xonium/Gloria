namespace Gloria.Data.Entities;

/// <summary>
/// A played-at date the user set for a demo. Keyed by content hash rather than demo id, because re-analysis deletes
/// and re-creates the demo; the importer applies it.
/// </summary>
public sealed class PlayedAtOverrideRecord
{
    public required string ContentHash { get; set; }

    public DateTimeOffset PlayedAt { get; set; }
}
