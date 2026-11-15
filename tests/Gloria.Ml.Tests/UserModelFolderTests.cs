using Gloria.Core.Classification;

namespace Gloria.Ml.Tests;

public sealed class UserModelFolderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("gloria-user-models-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void Promote_SwitchesToTheNewVersionAndKeepsOnePrevious()
    {
        Assert.Null(UserModelFolder.ActiveDirectory(_root.FullName));
        var bundle = Train(seed: 1);

        var first = UserModelFolder.Promote(_root.FullName, bundle);
        var second = UserModelFolder.Promote(_root.FullName, bundle);
        var third = UserModelFolder.Promote(_root.FullName, bundle);

        Assert.Equal(third, UserModelFolder.ActiveDirectory(_root.FullName));
        Assert.False(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
        Assert.IsType<MlAimClassifier>(AimClassifierFactory.Create(_root.FullName, Path.Combine(_root.FullName, "no-base")));
    }

    [Fact]
    public void ActiveDirectory_ReadsTheLayoutBeforeVersions()
    {
        AimModelStore.Save(Train(seed: 2), _root.FullName);

        Assert.Equal(_root.FullName, UserModelFolder.ActiveDirectory(_root.FullName));
    }

    [Fact]
    public void ActiveDirectory_IgnoresAPointerToAMissingFolder()
    {
        File.WriteAllText(Path.Combine(_root.FullName, UserModelFolder.CurrentFileName), "gone");

        Assert.Null(UserModelFolder.ActiveDirectory(_root.FullName));
    }

    [Fact]
    public void Agreement_CountsVerdictsTheClassifierShares()
    {
        var flagged = SyntheticEncounters.Neutral with { AngularErrorAtFirstShot = 6f };
        Assert.True(RuleBasedAimLabeler.IsFlagged(flagged, AimCategory.Hasty));
        Assert.False(RuleBasedAimLabeler.IsFlagged(SyntheticEncounters.Neutral, AimCategory.Hasty));

        var agreement = Agreement.Of(new RuleBasedAimClassifier(),
        [
            new CorrectedLabel(flagged, AimCategory.Hasty, true),
            new CorrectedLabel(SyntheticEncounters.Neutral, AimCategory.Hasty, true),
        ]);

        Assert.Equal(new Agreement(1, 2), agreement);
    }

    private static AimModelBundle Train(int seed) =>
        new AimModelTrainer(new AimTrainingOptions { NumberOfTrees = 5, Evaluation = EvaluationMode.HoldOut })
            .Train(SyntheticEncounters.Generate(400, seed));
}
