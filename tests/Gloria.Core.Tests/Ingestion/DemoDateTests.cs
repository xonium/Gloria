using Gloria.Core.Ingestion;
using Google.Protobuf;

namespace Gloria.Core.Tests.Ingestion;

public sealed class DemoDateTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("gloria-demo-date-");

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public void Find_UsesTheMatchTimeInTheInfoFile()
    {
        var demo = Path.Combine(_folder.FullName, "match.dem");
        File.WriteAllBytes(demo, [1, 2, 3]);
        var played = new DateTimeOffset(2026, 10, 3, 19, 45, 0, TimeSpan.Zero);
        File.WriteAllBytes(demo + ".info", new CDataGCCStrike15_v2_MatchInfo { Matchid = 42, Matchtime = (uint)played.ToUnixTimeSeconds() }.ToByteArray());

        var (playedAt, source) = DemoDate.Find(demo);

        Assert.Equal(PlayedAtSource.MatchInfo, source);
        Assert.Equal(played, playedAt);
    }

    [Fact]
    public void Find_FallsBackToTheEarliestFileTime()
    {
        var demo = Path.Combine(_folder.FullName, "copied.dem");
        File.WriteAllBytes(demo, [1, 2, 3]);
        var written = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(demo, written);
        File.SetCreationTimeUtc(demo, written.AddDays(30));

        var (playedAt, source) = DemoDate.Find(demo);

        Assert.Equal(PlayedAtSource.FileTime, source);
        Assert.Equal(new DateTimeOffset(written), playedAt);
    }

    [Fact]
    public void Find_IgnoresAnUnreadableInfoFile()
    {
        var demo = Path.Combine(_folder.FullName, "broken.dem");
        File.WriteAllBytes(demo, [1, 2, 3]);
        File.WriteAllBytes(demo + ".info", [0xff, 0xff, 0xff]);

        Assert.Equal(PlayedAtSource.FileTime, DemoDate.Find(demo).Source);
    }
}
