using Gloria.Core.Classification;
using Gloria.Ml;

namespace Gloria.Ml.Tests;

/// <summary>
/// Synthetic outcomes: the first shot hits more often the smaller the crosshair error, and the duel is
/// won exactly when IsKill is set (and independent of everything else). Six fake demos, as in _demos/.
/// </summary>
public sealed class OutcomeBundleFixture
{
    public const int DemoCount = 6;

    public OutcomeBundleFixture()
    {
        var random = new Random(11);
        Encounters = SyntheticEncounters.Features(1200, seed: 5)
            .Select((f, i) =>
            {
                var hitChance = 1 / (1 + Math.Exp(2.5 * (f.AngularErrorAtFirstShot - 1.5)));
                var labels = new OutcomeLabels(random.NextDouble() < hitChance, f.IsKill);
                return new OutcomeTrainingEncounter(f, $"demo{i % DemoCount}", labels);
            })
            .ToList();
        Bundle = new OutcomeModelTrainer().Train(Encounters);
    }

    public List<OutcomeTrainingEncounter> Encounters { get; }

    public OutcomeModelBundle Bundle { get; }

    public OutcomeModelInfo Info(OutcomeTarget target) => Bundle.Manifest.Targets.Single(t => t.Target == target);
}

public class OutcomeModelTrainerTests(OutcomeBundleFixture fixture) : IClassFixture<OutcomeBundleFixture>
{
    [Fact]
    public void WonDuel_CannotSeeIsKill_EvenWhenItEqualsTheLabel()
    {
        var info = fixture.Info(OutcomeTarget.WonDuel);

        Assert.DoesNotContain(nameof(MlEncounterRow.IsKill), info.FeatureNames);
        Assert.InRange(info.CrossValidation.MeanAuc!.Value, 0.35, 0.65);
    }

    [Fact]
    public void FirstShotHit_LeavesOutFeaturesMeasuredAfterTheShot()
    {
        var names = fixture.Info(OutcomeTarget.FirstShotHit).FeatureNames;

        Assert.DoesNotContain(nameof(MlEncounterRow.IsKill), names);
        Assert.DoesNotContain(nameof(MlEncounterRow.TimeToOnTarget), names);
        Assert.DoesNotContain(nameof(MlEncounterRow.ShotsWhileMovingRatio), names);
        Assert.Contains(nameof(MlEncounterRow.AngularErrorAtFirstShot), names);
    }

    [Fact]
    public void FirstShotHit_IsLearned_AndExplainedByTheCrosshairError()
    {
        var info = fixture.Info(OutcomeTarget.FirstShotHit);

        Assert.True(info.CrossValidation.MeanAuc > 0.75, $"AUC {info.CrossValidation.MeanAuc}");
        Assert.Equal(nameof(MlEncounterRow.AngularErrorAtFirstShot), info.Importance[0].Feature);
    }

    [Fact]
    public void CrossValidation_HasOneFoldPerDemo()
    {
        var folds = fixture.Info(OutcomeTarget.FirstShotHit).CrossValidation.Folds;

        Assert.Equal(OutcomeBundleFixture.DemoCount, folds.Count);
        Assert.Equal(folds.Count, folds.Select(f => f.HeldOut).Distinct().Count());
    }

    [Fact]
    public void Predict_NamesTheCrosshairError_AsTheCostOfAMissedFirstShot()
    {
        var predictor = new MlOutcomePredictor(fixture.Bundle);
        var wide = SyntheticEncounters.Neutral with { AngularErrorAtFirstShot = 6f };
        var tight = SyntheticEncounters.Neutral with { AngularErrorAtFirstShot = 0.2f };

        var predictions = predictor.Predict([wide, tight]);
        var wideHit = predictions[0].Single(p => p.Target == OutcomeTarget.FirstShotHit);
        var tightHit = predictions[1].Single(p => p.Target == OutcomeTarget.FirstShotHit);

        Assert.True(wideHit.Probability < tightHit.Probability);
        Assert.Equal(nameof(MlEncounterRow.AngularErrorAtFirstShot), wideHit.Costs[0].Feature);
    }

    [Fact]
    public void SaveAndLoad_GivesTheSamePredictions()
    {
        using var dir = new TempDirectory();
        OutcomeModelStore.Save(fixture.Bundle, dir.Path);
        var loaded = OutcomeModelStore.Load(dir.Path);
        var sample = SyntheticEncounters.Features(20, seed: 99);

        var before = new MlOutcomePredictor(fixture.Bundle).Predict(sample);
        var after = new MlOutcomePredictor(loaded).Predict(sample);

        Assert.Equal(fixture.Bundle.Manifest.ModelVersion, loaded.Manifest.ModelVersion);
        Assert.Equal(
            before.SelectMany(p => p.Select(x => x.Probability)),
            after.SelectMany(p => p.Select(x => x.Probability)));
        Assert.Equal(OutcomeBundleFixture.DemoCount, loaded.Manifest.Targets[0].CrossValidation.Folds.Count);
    }
}

public class FeatureBinReportTests
{
    [Fact]
    public void BestThreshold_RecoversAPlantedCut()
    {
        var random = new Random(3);
        var rows = Enumerable.Range(0, 4000)
            .Select(_ => (float)(random.NextDouble() * 6))
            .Select(v => (v, random.NextDouble() < (v < 2f ? 0.7 : 0.2)))
            .ToList();
        var candidates = Enumerable.Range(1, 23).Select(i => i * 0.25f);

        var scan = FeatureBinReport.BestThreshold(rows, candidates);

        Assert.NotNull(scan);
        Assert.Equal(2f, scan.Threshold);
        Assert.True(scan.RateBelow > scan.RateAbove);
    }

    [Fact]
    public void FixedBins_CountRowsInHalfOpenIntervals_AndTheLastBinIsOpen()
    {
        (float, bool)[] rows = [(0f, true), (0.99f, false), (1f, true), (50f, true)];

        var bins = FeatureBinReport.Fixed(rows, [0f, 1f, 2f]);

        Assert.Equal([2, 1, 1], bins.Select(b => b.Count));
        Assert.Equal(0.5, bins[0].Rate);
        Assert.Equal(float.PositiveInfinity, bins[^1].Upper);
    }

    [Fact]
    public void Wilson_ContainsTheRate()
    {
        var (low, high) = FeatureBinReport.Wilson(30, 100);

        Assert.InRange(0.3, low, high);
        Assert.True(low > 0.2 && high < 0.4);
    }

    [Fact]
    public void LeaveOneGroupOut_HoldsOutEachGroupOnce()
    {
        string[] rows = ["a1", "a2", "b1", "c1", "c2"];

        var folds = CrossValidation.LeaveOneGroupOut(rows, r => r[..1]).ToList();

        Assert.Equal(["a", "b", "c"], folds.Select(f => f.HeldOut));
        Assert.All(folds, f => Assert.All(f.Test, r => Assert.StartsWith(f.HeldOut, r)));
        Assert.All(folds, f => Assert.Equal(rows.Length, f.Train.Count + f.Test.Count));
    }
}
