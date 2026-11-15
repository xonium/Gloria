using Gloria.Core.Aggregation;

namespace Gloria.Core.Tests.Aggregation;

public class RollingTests
{
    [Fact]
    public void Median_IgnoresOneOddValue()
    {
        var smoothed = Rolling.Median([0.5, 0.6, 0.55, 0.1, 0.6, 0.58, 0.62], window: 5);

        Assert.True(double.IsNaN(smoothed[0]));
        Assert.True(double.IsNaN(smoothed[1]));
        Assert.Equal(0.55, smoothed[2]);
        Assert.Equal(0.55, smoothed[3]);
        Assert.Equal(0.55, smoothed[4]);
        Assert.Equal(0.58, smoothed[5]);
        Assert.Equal(0.58, smoothed[6]);
    }

    [Fact]
    public void Median_SkipsMissingValues()
    {
        var smoothed = Rolling.Median([1, double.NaN, 3, double.NaN, 5], window: 5);

        Assert.True(double.IsNaN(smoothed[1]));
        Assert.Equal(3, smoothed[2]);
        Assert.Equal(3, smoothed[4]);
    }

    [Fact]
    public void Median_ShortSeriesStillGetsAValue()
    {
        Assert.Equal([4.0], Rolling.Median([4.0], window: 5));
        Assert.Equal(4, Rolling.Median([2.0, 4.0], window: 5)[1]);
    }
}
