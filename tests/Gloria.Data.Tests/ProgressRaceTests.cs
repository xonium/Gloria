using Gloria.Data.Queries;

namespace Gloria.Data.Tests;

public sealed class ProgressRaceTests
{
    private const double N = double.NaN;

    [Fact]
    public void Leader_IsTheBestLatestMedianWhicheverWayIsBetter()
    {
        double[][] trends = [[N, 0.4, 0.5], [N, 0.6, 0.3], [N, N, 0.45]];

        var higher = ProgressRace.Rank(trends, higherIsBetter: true, window: 5);
        var lower = ProgressRace.Rank(trends, higherIsBetter: false, window: 5);

        Assert.Equal([0], higher.Leaders);
        Assert.Equal(0.5, higher.Best!.Value, 9);
        Assert.Equal([1], lower.Leaders);
        Assert.Equal(0.3, lower.Best!.Value, 9);
    }

    [Fact]
    public void Leader_ReportsEveryPlayerOnATie_AndSkipsPlayersWithoutAMedian()
    {
        double[][] trends = [[0.5], [N, N], [N, 0.5], []];

        var standing = ProgressRace.Rank(trends, higherIsBetter: true, window: 5);

        Assert.Equal([0, 2], standing.Leaders);
    }

    [Fact]
    public void NoMedians_NoLeaderAndNotEnoughDemos()
    {
        var standing = ProgressRace.Rank([[N, N], []], higherIsBetter: true, window: 5);

        Assert.Empty(standing.Leaders);
        Assert.Null(standing.Best);
        Assert.Equal(Improvement.NotEnoughDemos, standing.Improvement);
        Assert.Null(standing.MostImproved);
    }

    [Fact]
    public void MostImproved_NeedsMediansAWindowApart()
    {
        // Player 0: first and latest medians 4 demos apart, so they overlap; player 1: 5 apart.
        double[][] trends =
        [
            [N, N, 0.1, 0.2, 0.3, 0.4, 0.9],
            [N, 0.5, 0.5, 0.5, 0.5, 0.5, 0.6],
        ];

        var standing = ProgressRace.Rank(trends, higherIsBetter: true, window: 5);

        Assert.Equal(Improvement.Improved, standing.Improvement);
        Assert.Equal(1, standing.MostImproved);
        Assert.Equal(0.1, standing.Change!.Value, 9);
    }

    [Fact]
    public void MostImproved_WhenLowerIsBetter_IsTheLargestDrop()
    {
        double[][] trends =
        [
            [0.6, N, N, N, N, 0.5],
            [0.7, N, N, N, N, 0.4],
            [0.3, N, N, N, N, 0.5],
        ];

        var standing = ProgressRace.Rank(trends, higherIsBetter: false, window: 5);

        Assert.Equal(1, standing.MostImproved);
        Assert.Equal(-0.3, standing.Change!.Value, 9);
        Assert.Equal([1], standing.Leaders);
    }

    [Fact]
    public void MostImproved_NoneWhenEveryoneStayedLevelOrGotWorse()
    {
        double[][] trends = [[0.5, N, N, N, N, 0.5], [0.6, N, N, N, N, 0.4]];

        var standing = ProgressRace.Rank(trends, higherIsBetter: true, window: 5);

        Assert.Equal(Improvement.NoOneImproved, standing.Improvement);
        Assert.Null(standing.MostImproved);
        Assert.Null(standing.Change);
    }

    [Fact]
    public void MostImproved_WorksWithOnePlayerWhoQualifies()
    {
        double[][] trends = [[0.2, N, N, N, N, 0.5], [N, 0.9]];

        var standing = ProgressRace.Rank(trends, higherIsBetter: true, window: 5);

        Assert.Equal(0, standing.MostImproved);
        Assert.Equal([1], standing.Leaders);
    }
}
