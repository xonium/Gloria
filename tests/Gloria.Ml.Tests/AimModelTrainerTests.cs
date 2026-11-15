using System.Text.Json.Nodes;
using Gloria.Core.Classification;

namespace Gloria.Ml.Tests;

public class AimModelTrainerTests(TrainedBundleFixture fixture) : IClassFixture<TrainedBundleFixture>
{
    [Fact]
    public void Train_ReachesHighAuc_OnHeldOutDemos_ForEveryCategory()
    {
        var categories = fixture.Bundle.Manifest.Categories;

        Assert.Equal(Enum.GetValues<AimCategory>(), categories.Select(c => c.Category));
        Assert.All(categories, c => Assert.True(c.Auc > 0.9, $"{c.Category} AUC {c.Auc}"));
        Assert.Equal(EncounterFeatures.SchemaVersion, fixture.Bundle.Manifest.FeatureSchemaVersion);
    }

    [Fact]
    public void Classify_AgreesWithTheRules_ItWasTrainedOn()
    {
        var encounters = SyntheticEncounters.Features(500, seed: 99);
        var predictions = new MlAimClassifier(fixture.Bundle).Classify(encounters);

        foreach (var category in Enum.GetValues<AimCategory>())
        {
            var agreement = encounters.Zip(predictions)
                .Average(p => p.Second[category].Flagged == RuleBasedAimLabeler.IsFlagged(p.First, category) ? 1.0 : 0.0);
            Assert.True(agreement > 0.95, $"{category} agreement {agreement:P1}");
        }
    }

    [Fact]
    public void Classify_ExplainsMovingShot_BySpeedAtFirstShot()
    {
        var moving = SyntheticEncounters.Neutral with { SpeedRatioAtFirstShot = 1.8f };

        var score = new MlAimClassifier(fixture.Bundle).Classify([moving])[0][AimCategory.MovingShot];

        Assert.True(score.Flagged);
        Assert.Equal(nameof(MlEncounterRow.SpeedRatioAtFirstShot), score.TopContributions[0].Feature);
    }

    [Fact]
    public void SaveAndLoad_GivesTheSamePredictions()
    {
        using var directory = new TempDirectory();
        var encounters = SyntheticEncounters.Features(50, seed: 3);

        AimModelStore.Save(fixture.Bundle, directory.Path);
        var loaded = AimModelStore.Load(directory.Path);

        var before = new MlAimClassifier(fixture.Bundle).Classify(encounters);
        var after = new MlAimClassifier(loaded).Classify(encounters);
        Assert.Equal(fixture.Bundle.Manifest.ModelVersion, loaded.Manifest.ModelVersion);
        Assert.Equal(
            before.SelectMany(c => c.Scores.Select(s => s.Probability)),
            after.SelectMany(c => c.Scores.Select(s => s.Probability)),
            (a, b) => Math.Abs(a - b) < 1e-5f);
    }

    [Fact]
    public void Load_RefusesABundle_ForAnotherFeatureSchema()
    {
        using var directory = new TempDirectory();
        AimModelStore.Save(fixture.Bundle, directory.Path);
        var manifestPath = Path.Combine(directory.Path, AimModelStore.ManifestFileName);
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["FeatureSchemaVersion"] = EncounterFeatures.SchemaVersion + 1;
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        Assert.Throws<AimModelException>(() => AimModelStore.Load(directory.Path));

        var rejections = new List<string>();
        var classifier = AimClassifierFactory.Create(directory.Path, Path.Combine(directory.Path, "missing"), rejections.Add);
        Assert.IsType<RuleBasedAimClassifier>(classifier);
        Assert.Single(rejections);
    }

    [Fact]
    public void Factory_PrefersTheUserModel_ThenFallsBackToRules()
    {
        using var directory = new TempDirectory();
        var user = Path.Combine(directory.Path, "user");
        var shipped = Path.Combine(directory.Path, "base");

        Assert.IsType<RuleBasedAimClassifier>(AimClassifierFactory.Create(user, shipped));

        AimModelStore.Save(fixture.Bundle, user);
        var classifier = Assert.IsType<MlAimClassifier>(AimClassifierFactory.Create(user, shipped));
        Assert.Equal($"model {fixture.Bundle.Manifest.ModelVersion}", classifier.Source);
    }

    [Fact]
    public void Corrections_OverrideTheRules_ForTheRegionTheyCover()
    {
        // The user says late first shots at long range are not slow reactions (they were holding an angle).
        static bool LongRangeLate(EncounterFeatures f) =>
            f.DistanceToEnemy > 2500f && f.TimeToFirstShot > RuleBasedAimLabeler.SlowReactionSeconds;

        var encounters = SyntheticEncounters.Generate(2000, seed: 11)
            .Select(e => LongRangeLate(e.Features)
                ? e with { Corrections = new Dictionary<AimCategory, bool> { [AimCategory.SlowReaction] = false } }
                : e)
            .ToList();

        var classifier = new MlAimClassifier(new AimModelTrainer().Train(encounters));

        var longRange = SyntheticEncounters.Neutral with { TimeToFirstShot = 0.85f, DistanceToEnemy = 3000f };
        var shortRange = longRange with { DistanceToEnemy = 800f };
        var results = classifier.Classify([longRange, shortRange]);

        Assert.True(RuleBasedAimLabeler.IsFlagged(longRange, AimCategory.SlowReaction));
        Assert.False(results[0][AimCategory.SlowReaction].Flagged);
        Assert.True(results[1][AimCategory.SlowReaction].Flagged);
    }
}
