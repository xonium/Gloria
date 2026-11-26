using Gloria.Core.Encounters;

namespace Gloria.Core.Tests.Encounters;

public class TimeToKillTests
{
    [Fact]
    public void Seconds_FromFirstShotToTheKill()
    {
        Assert.Equal(0.5, TimeToKill.Seconds(EncounterOutcome.Kill, firstShotTick: 1000, endTick: 1032, tickRate: 64));
    }

    [Fact]
    public void Seconds_ZeroForAOneShotKill()
    {
        Assert.Equal(0, TimeToKill.Seconds(EncounterOutcome.Kill, firstShotTick: 1000, endTick: 1000, tickRate: 64));
    }

    [Theory]
    [InlineData(EncounterOutcome.Death)]
    [InlineData(EncounterOutcome.Disengage)]
    [InlineData(EncounterOutcome.Timeout)]
    public void Seconds_OnlyForKills(EncounterOutcome outcome)
    {
        Assert.Null(TimeToKill.Seconds(outcome, firstShotTick: 1000, endTick: 1032, tickRate: 64));
    }

    [Fact]
    public void Seconds_NullWithoutAUsableTickRateOrOrder()
    {
        Assert.Null(TimeToKill.Seconds(EncounterOutcome.Kill, firstShotTick: 1032, endTick: 1000, tickRate: 64));
        Assert.Null(TimeToKill.Seconds(EncounterOutcome.Kill, firstShotTick: 1000, endTick: 1032, tickRate: 0));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(99, false)]
    [InlineData(27, false)]
    public void Counts_OnlyFullHealthOpponents(int health, bool counts)
    {
        Assert.Equal(counts, TimeToKill.Counts(health));
    }
}
