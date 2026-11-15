namespace Gloria.Data.Entities;

/// <summary>An imported demo, keyed by the SHA-256 of its contents.</summary>
public sealed class DemoRecord
{
    public int Id { get; set; }

    /// <summary>Lowercase hex SHA-256 of the .dem file. Unique.</summary>
    public required string ContentHash { get; set; }

    public required string FileName { get; set; }

    public string MapName { get; set; } = "";

    public DateTimeOffset? PlayedAt { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>Parser version that produced the stored data; re-parse when it is behind.</summary>
    public int ParserVersion { get; set; }

    /// <summary>Metric code version of the stored analysis; re-analyze when it is behind.</summary>
    public int AnalysisVersion { get; set; }

    public List<DemoPlayerRecord> Players { get; set; } = [];
}
