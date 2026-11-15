using Gloria.Core.Ingestion;
using Gloria.Core.Tests.Fixtures;

namespace Gloria.Core.Tests.Ingestion;

[Trait("Category", DemoFiles.Category)]
public class DemoFileDemoParserTests
{
    [Fact]
    public async Task ParseAsync_ReadsMapPlayersAndEvents()
    {
        var path = DemoFiles.FirstOrSkip();
        await using var stream = File.OpenRead(path);

        var demo = await new DemoFileDemoParser().ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(demo.MapName));
        Assert.True(demo.TickCount > 0);
        Assert.True(demo.Players.Count >= 10);
        Assert.Contains(demo.Events, e => e is RoundEndEvent);
        Assert.Contains(demo.Events, e => e is WeaponFireEvent);
        Assert.Contains(demo.Events, e => e is PlayerDeathEvent);
    }
}
