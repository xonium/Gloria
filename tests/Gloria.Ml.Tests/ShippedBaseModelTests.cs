using Gloria.Ml;

namespace Gloria.Ml.Tests;

/// <summary>
/// The base model committed in src/Gloria/models/base must load with the current feature schema;
/// otherwise the app silently falls back to the rules. Retrain with tools/Gloria.Trainer after a schema bump.
/// </summary>
public class ShippedBaseModelTests
{
    private static string BaseModelDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gloria.slnx")))
                return Path.Combine(dir.FullName, "src", "Gloria", "models", "base");
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void AimModel_LoadsAndIsPickedByTheFactory()
    {
        var directory = BaseModelDirectory();
        Assert.SkipUnless(AimModelStore.Exists(directory), "No base model has been published yet.");
        var rejections = new List<string>();

        var classifier = AimClassifierFactory.Create(Path.Combine(directory, "no-user-model"), directory, rejections.Add);

        Assert.Empty(rejections);
        Assert.IsType<MlAimClassifier>(classifier);
        Assert.Equal(5, classifier.Classify(SyntheticEncounters.Features(5, seed: 1)).Count);
    }

    [Fact]
    public void OutcomeModel_LoadsAndPredictsEveryTarget()
    {
        var directory = Path.Combine(BaseModelDirectory(), OutcomeModelStore.SubfolderName);
        Assert.SkipUnless(OutcomeModelStore.Exists(directory), "No base outcome model has been published yet.");

        var predictor = new MlOutcomePredictor(OutcomeModelStore.Load(directory));
        var predictions = predictor.Predict(SyntheticEncounters.Features(3, seed: 2));

        Assert.All(predictions, p => Assert.Equal(Enum.GetValues<OutcomeTarget>().Length, p.Count));
    }

    [Fact]
    public void OutcomeModel_IsPickedByTheFactory()
    {
        var directory = BaseModelDirectory();
        Assert.SkipUnless(OutcomeModelStore.Exists(Path.Combine(directory, OutcomeModelStore.SubfolderName)), "No base outcome model has been published yet.");
        var rejections = new List<string>();

        var predictor = OutcomePredictorFactory.Create(Path.Combine(directory, "no-user-model"), directory, rejections.Add);

        Assert.Empty(rejections);
        Assert.NotNull(predictor);
        var prediction = Assert.Single(predictor.Predict(SyntheticEncounters.Features(1, seed: 3)));
        Assert.All(prediction, p => Assert.InRange(p.Probability, 0f, 1f));
    }

    [Fact]
    public void OutcomeFactory_ReturnsNullWithoutModels()
    {
        var missing = Path.Combine(Path.GetTempPath(), "gloria-no-models-" + Guid.NewGuid().ToString("N"));

        Assert.Null(OutcomePredictorFactory.Create(Path.Combine(missing, "user"), Path.Combine(missing, "base")));
    }
}
