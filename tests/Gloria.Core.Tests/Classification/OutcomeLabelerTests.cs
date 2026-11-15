using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;
using Gloria.Core.Tests.Fixtures;
using static Gloria.Core.Tests.Fixtures.Scenarios;

namespace Gloria.Core.Tests.Classification;

public class OutcomeLabelerTests
{
    private static OutcomeLabels LabelsFor(ParsedDemo demo)
    {
        var encounter = Assert.Single(new EncounterSegmenter().Segment(demo, A));
        var metrics = new AimMetricsCalculator().Calculate(demo, encounter)!;
        return new OutcomeLabeler().Label(demo, encounter, metrics);
    }

    [Fact]
    public void FirstShotHit_IsTrue_WhenDamageFollowsWithinTheHitWindow()
    {
        var demo = Duel().Spotted(B, by: A, 100, 130).Fire(110, A).Hurt(111, B, A).Build(300);

        Assert.True(LabelsFor(demo).FirstShotHit);
    }

    [Fact]
    public void FirstShotHit_IsFalse_WithoutDamage()
    {
        var demo = Duel().Spotted(B, by: A, 100, 130).Fire(110, A).Fire(118, A).Build(300);

        Assert.False(LabelsFor(demo).FirstShotHit);
    }

    [Fact]
    public void FirstShotHit_IsNull_WhenTheNextShotCameBeforeTheDamage()
    {
        var demo = Duel().Spotted(B, by: A, 100, 130).Fire(110, A).Fire(111, A).Hurt(112, B, A).Build(300);

        Assert.Null(LabelsFor(demo).FirstShotHit);
    }

    [Fact]
    public void FirstShotHit_IgnoresGrenadeDamage()
    {
        var demo = Duel().Spotted(B, by: A, 100, 130).Fire(110, A).Hurt(110, B, A, "hegrenade").Build(300);

        Assert.False(LabelsFor(demo).FirstShotHit);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WonDuel_FollowsTheKillOrDeath(bool aWins)
    {
        var builder = Duel().Spotted(B, by: A, 100, 130).Fire(110, A);
        var demo = (aWins ? builder.Death(115, B, A) : builder.Death(115, A, B)).Build(300);

        Assert.Equal(aWins, LabelsFor(demo).WonDuel);
    }

    [Fact]
    public void WonDuel_IsNull_ForADisengage()
    {
        var demo = Duel().Spotted(B, by: A, 100, 130).Fire(110, A).Build(600);

        Assert.Null(LabelsFor(demo).WonDuel);
    }
}
