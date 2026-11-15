namespace Gloria.Core.Ingestion;

/// <summary>Parses a CS2 demo into normalized per-tick state and events.</summary>
public interface IDemoParser
{
    /// <summary>Bumped when the parser's output changes, so cached parses can be invalidated.</summary>
    int Version { get; }

    /// <param name="progress">Receives progress in the range 0..1.</param>
    Task<ParsedDemo> ParseAsync(Stream demo, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
