using Gloria.Core.Ingestion;

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

    /// <summary>
    /// The demo doesn't record when it was played, so this comes from Valve's .dem.info file if there is one,
    /// else from the file's timestamps. See <see cref="DemoDate"/>.
    /// </summary>
    public DateTimeOffset? PlayedAt { get; set; }

    public PlayedAtSource PlayedAtSource { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    public int TickRate { get; set; }

    /// <summary>Parser version that produced the stored data; re-parse when it is behind.</summary>
    public int ParserVersion { get; set; }

    /// <summary>Metric code version of the stored analysis; re-analyze when it is behind.</summary>
    public int AnalysisVersion { get; set; }

    /// <summary><c>DemoImporter.AnalysisVersionKey</c> of the stored encounters, e.g. "p7-s3-m6-f3-l1-r1".</summary>
    public string AnalysisVersionKey { get; set; } = "";

    public List<DemoPlayerRecord> Players { get; set; } = [];

    public List<RoundRecord> Rounds { get; set; } = [];

    public List<EncounterRecord> Encounters { get; set; } = [];

    public List<SprayBulletRecord> SprayBullets { get; set; } = [];
}
