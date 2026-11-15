using Gloria.Core.Ingestion;

namespace Gloria.Core.Tests.Ingestion;

public class RoundTrackerTests
{
    [Fact]
    public void KeepsTheFirstRoundWhenTheDemoStartsInsideIt()
    {
        // As in the real demos: recording starts in round 1's freeze time, with no round start.
        var tracker = new RoundTracker();
        tracker.FreezeEnd(895);
        tracker.MatchStart(895);
        tracker.End(4166, Team.Terrorist);
        tracker.Start(4614, warmup: false);
        tracker.FreezeEnd(5510);
        tracker.End(10327, Team.CounterTerrorist);

        Assert.Equal(
            [new RoundInfo(1, 895, 895, 4166, Team.Terrorist), new RoundInfo(2, 4614, 5510, 10327, Team.CounterTerrorist)],
            tracker.Rounds);
    }

    [Fact]
    public void KeepsTheFirstRoundWhenTheMatchStartIsAnnouncedDuringIt()
    {
        var tracker = new RoundTracker();
        tracker.Start(100, warmup: true);
        tracker.End(200, Team.Terrorist);
        tracker.Start(300, warmup: false);
        tracker.FreezeEnd(400);
        tracker.MatchStart(400);
        tracker.End(900, Team.CounterTerrorist);

        Assert.Equal([new RoundInfo(1, 300, 400, 900, Team.CounterTerrorist)], tracker.Rounds);
    }

    [Fact]
    public void DropsWarmupAndRoundsBeforeARestart()
    {
        var tracker = new RoundTracker();
        tracker.Start(100, warmup: true);
        tracker.FreezeEnd(150);
        tracker.End(200, Team.Terrorist);
        tracker.Start(300, warmup: false);
        tracker.FreezeEnd(350);
        tracker.MatchStart(350);
        tracker.End(400, Team.Terrorist);
        tracker.Start(500, warmup: false);
        tracker.FreezeEnd(550);
        tracker.MatchStart(550);
        tracker.End(600, Team.CounterTerrorist);

        Assert.Equal([new RoundInfo(1, 500, 550, 600, Team.CounterTerrorist)], tracker.Rounds);
    }

    [Fact]
    public void AMatchStartWithoutAFreezeEndLeavesTheFreezeEndAtTheStart()
    {
        var tracker = new RoundTracker();
        tracker.MatchStart(1000);
        tracker.End(2000, Team.Terrorist);

        Assert.Equal([new RoundInfo(1, 1000, 1000, 2000, Team.Terrorist)], tracker.Rounds);
    }
}
