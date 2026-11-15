namespace Gloria.Data.Entities;

/// <summary>An imported demo, keyed by the SHA-256 of its contents.</summary>
public sealed class DemoRecord
{
    public int Id { get; set; }

    /// <summary>Lowercase hex SHA-256 of the .dem file. Unique.</summary>
    public required string ContentHash { get; set; }

    public required string FileName { get; set; }

    /// <summary>Where the file was when it was imported. Used for re-analysis, if the file is still there.</summary>
    public string? FilePath { get; set; }

    public long FileSize { get; set; }

    public string MapName { get; set; } = "";

    /// <summary>The demo doesn't record when it was played, so this is the file's last write time.</summary>
    public DateTimeOffset? PlayedAt { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    public int TickRate { get; set; }

    /// <summary>Parser version that produced the stored data; re-parse when it is behind.</summary>
    public int ParserVersion { get; set; }

    /// <summary>Metric code version of the stored analysis; re-analyze when it is behind.</summary>
    public int AnalysisVersion { get; set; }

    /// <summary><c>DemoAnalyzer.VersionKey</c> of the stored encounters, e.g. "s2-m5-f2-l1".</summary>
    public string AnalysisVersionKey { get; set; } = "";

    public List<DemoPlayerRecord> Players { get; set; } = [];

    public List<RoundRecord> Rounds { get; set; } = [];

    public List<EncounterRecord> Encounters { get; set; } = [];
}
