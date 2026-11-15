namespace Gloria.Core.Ingestion;

/// <summary>Where a demo's played-at date came from.</summary>
public enum PlayedAtSource
{
    /// <summary>The file's timestamps. Copying or downloading demos together gives them all the same date.</summary>
    FileTime,

    /// <summary>The match time in Valve's <c>.dem.info</c> file next to the demo.</summary>
    MatchInfo,
}

/// <summary>
/// Finds when a demo was played. The .dem itself records no wall-clock time, so this uses the match time in
/// the <c>.dem.info</c> file Valve's matchmaking downloads come with, and otherwise the file's timestamps.
/// </summary>
public static class DemoDate
{
    public static (DateTimeOffset PlayedAt, PlayedAtSource Source) Find(string demoPath)
    {
        if (FromMatchInfo(demoPath + ".info") is { } matchTime)
            return (matchTime, PlayedAtSource.MatchInfo);

        // Copying a file keeps its last write time but gives it a new creation time; downloading sets both.
        // The earlier of the two is the closer guess.
        var file = new FileInfo(demoPath);
        var earliest = file.CreationTimeUtc < file.LastWriteTimeUtc ? file.CreationTimeUtc : file.LastWriteTimeUtc;
        return (new DateTimeOffset(earliest, TimeSpan.Zero), PlayedAtSource.FileTime);
    }

    /// <summary>Reads <c>matchtime</c> from a <c>CDataGCCStrike15_v2_MatchInfo</c> file; null if it is missing or unreadable.</summary>
    public static DateTimeOffset? FromMatchInfo(string infoPath)
    {
        try
        {
            if (!File.Exists(infoPath))
                return null;

            var info = CDataGCCStrike15_v2_MatchInfo.Parser.ParseFrom(File.ReadAllBytes(infoPath));
            return info.HasMatchtime && info.Matchtime > 0 ? DateTimeOffset.FromUnixTimeSeconds(info.Matchtime) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Google.Protobuf.InvalidProtocolBufferException)
        {
            return null;
        }
    }
}
