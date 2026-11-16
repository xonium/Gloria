using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Data.Corrections;
using Gloria.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data.Tests;

public sealed class CorrectionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestDbContextFactory _factory;
    private readonly CorrectionStore _store;

    public CorrectionTests()
    {
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
        DatabaseInitializer.Migrate(_factory);
        _store = new CorrectionStore(_factory);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Set_OverridesTheModelAndClearingRestoresIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await AddDemoAsync("aa", hastyProbability: 0.9f);

        var flagsOff = await _store.SetAsync(id, AimCategory.Hasty, false, ct);
        var flagsOn = await _store.SetAsync(id, AimCategory.Overshoot, true, ct);

        Assert.Equal(0, flagsOff!.Value.Flags);
        Assert.Equal(1 << (int)AimCategory.Overshoot, flagsOn!.Value.Flags);
        Assert.Equal(AimCategory.Overshoot, flagsOn.Value.Primary);
        Assert.Equal(new Dictionary<AimCategory, bool> { [AimCategory.Hasty] = false, [AimCategory.Overshoot] = true }, await _store.ForEncounterAsync(id, ct));
        Assert.Equal(2, await _store.CountAsync(ct: ct));

        var cleared = await _store.SetAsync(id, AimCategory.Hasty, null, ct);

        Assert.Equal((1 << (int)AimCategory.Hasty) | (1 << (int)AimCategory.Overshoot), cleared!.Value.Flags);
        Assert.Equal(AimCategory.Hasty, cleared.Value.Primary);
        Assert.Equal(1, await _store.CountAsync(ct: ct));
    }

    [Fact]
    public async Task Corrections_SurviveReanalysisAndReachTheTrainingSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await AddDemoAsync("bb", hastyProbability: 0.9f);
        await _store.SetAsync(id, AimCategory.Hasty, false, ct);

        // Re-analysis deletes the demo and stores it again with new encounter ids.
        await using (var db = _factory.CreateDbContext())
        {
            db.Demos.RemoveRange(db.Demos);
            await db.SaveChangesAsync(ct);
        }

        // Re-analysis can renumber rounds (parser version 5 added the first round); the correction must still apply.
        var newId = await AddDemoAsync("bb", hastyProbability: 0.9f, round: 4);
        Assert.NotEqual(id, newId);
        Assert.Equal(new Dictionary<AimCategory, bool> { [AimCategory.Hasty] = false }, await _store.ForEncounterAsync(newId, ct));

        // Reclassifying with the rules re-applies the correction over the new scores.
        Assert.Equal(1, await _store.ReclassifyAsync(new RuleBasedAimClassifier(), ct: ct));
        await using (var db = _factory.CreateDbContext())
        {
            var e = await db.Encounters.SingleAsync(ct);
            Assert.Equal("rules", e.ClassifierSource);
            Assert.False(e.Has(AimCategory.Hasty));
        }

        var row = Assert.Single(await _store.TrainingSetAsync(ct));
        Assert.Equal("bb", row.DemoHash);
        Assert.False(row.Corrections![AimCategory.Hasty]);
        Assert.Equal(0, await _store.ReclassifyAsync(new RuleBasedAimClassifier(), ct: ct));
    }

    [Fact]
    public async Task Count_SinceOnlyCountsLaterVerdicts()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await AddDemoAsync("cc", hastyProbability: 0.1f);
        await _store.SetAsync(id, AimCategory.Hasty, true, ct);
        var between = DateTimeOffset.UtcNow;
        await Task.Delay(20, ct);
        await _store.SetAsync(id, AimCategory.Overshoot, true, ct);

        Assert.Equal(1, await _store.CountAsync(between, ct));
    }

    [Fact]
    public async Task TrainingSet_LeavesOutEncountersAnalyzedBeforeThePreAimErrorWasMeasured()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddDemoAsync("old", hastyProbability: 0.9f, preAimError: null);
        await AddDemoAsync("new", hastyProbability: 0.9f, preAimError: 20f);

        var row = Assert.Single(await _store.TrainingSetAsync(ct));
        Assert.Equal("new", row.DemoHash);
        Assert.Equal(20f, row.Features.PreAimErrorDegrees);
    }

    [Fact]
    public async Task Reclassify_StoresTheFlickScores()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await AddDemoAsync("dd", hastyProbability: 0f, preAimError: 30f);

        await _store.ReclassifyAsync(new RuleBasedAimClassifier(), ct: ct);

        await using var db = _factory.CreateDbContext();
        var e = await db.Encounters.SingleAsync(x => x.Id == id, ct);
        Assert.Equal(1f, e.MediumFlickProbability);
        Assert.Equal(0f, e.SmallFlickProbability);
        Assert.Equal(0f, e.LargeFlickProbability);
        Assert.True(e.Has(AimCategory.MediumFlick));
        Assert.Equal(30f, e.PreAimErrorDegrees);
    }

    private async Task<int> AddDemoAsync(string hash, float hastyProbability, int round = 3, float? preAimError = 2f)
    {
        var encounter = new EncounterRecord
        {
            PlayerSteamId = 1,
            OpponentSteamId = 2,
            RoundNumber = round,
            StartTick = 1000,
            EndTick = 1200,
            FirstShotTick = 1050,
            Outcome = EncounterOutcome.Kill,
            FirstShotWeapon = "ak47",
            OpponentWeapon = "m4a1",
            ShotCount = 1,
            HastyProbability = hastyProbability,
            PreAimErrorDegrees = preAimError,
            ClassifierSource = "model test",
        };
        encounter.ApplyCorrections(null);
        var demo = new DemoRecord { ContentHash = hash, FileName = $"{hash}.dem", ImportedAt = DateTimeOffset.UnixEpoch };
        demo.Encounters.Add(encounter);

        await using var db = _factory.CreateDbContext();
        db.Demos.Add(demo);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return encounter.Id;
    }

    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<GloriaDbContext>
    {
        public GloriaDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<GloriaDbContext>().UseSqlite(connection).Options);
    }
}
