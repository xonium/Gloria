using Gloria.Core.Analysis;
using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Data.Entities;
using Gloria.Data.Import;
using Gloria.Data.Queries;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data.Tests;

public sealed class ImportTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestDbContextFactory _factory;

    public ImportTests()
    {
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
        DatabaseInitializer.Migrate(_factory);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void TraceCodec_RoundTrips()
    {
        var trace = new EncounterTrace(
            1000,
            64,
            [1.5f, -0.25f],
            [0.5f, 0f],
            [1.2f, -0.1f],
            [210f, 12f],
            [1420f, 1418f],
            [new TraceShot(1001, true, 2), new TraceShot(1001, false, null)]);

        var decoded = TraceCodec.Decode(TraceCodec.Encode(trace));

        Assert.NotNull(decoded);
        Assert.Equal(1000, decoded.StartTick);
        Assert.Equal(64, decoded.TickRate);
        Assert.Equal(trace.ErrorX, decoded.ErrorX);
        Assert.Equal(trace.Speed, decoded.Speed);
        Assert.Equal(trace.Shots, decoded.Shots);
    }

    [Fact]
    public async Task Matches_CountRoundsFromThePlayersSide()
    {
        const ulong player = 76561198000000001;
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _factory.CreateDbContext())
        {
            var demo = new DemoRecord { ContentHash = "aa", FileName = "a.dem", MapName = "de_mirage", AnalysisVersionKey = "v" };
            demo.Players.Add(new DemoPlayerRecord { SteamId = player, Name = "me", TeamsByRound = "CCT" });
            demo.Rounds.Add(new RoundRecord { Number = 1, Winner = Team.CounterTerrorist });
            demo.Rounds.Add(new RoundRecord { Number = 2, Winner = Team.Terrorist });
            demo.Rounds.Add(new RoundRecord { Number = 3, Winner = Team.Terrorist });
            demo.Encounters.Add(new EncounterRecord { PlayerSteamId = player, RoundNumber = 1, Outcome = EncounterOutcome.Kill, Flags = 1 << (int)AimCategory.Hasty });
            demo.Encounters.Add(new EncounterRecord { PlayerSteamId = 2, RoundNumber = 1, Outcome = EncounterOutcome.Death });
            db.Demos.Add(demo);
            await db.SaveChangesAsync(ct);
        }

        var match = Assert.Single(await new GloriaReadModel(_factory, "v").MatchesAsync(player, ct));

        Assert.Equal(2, match.RoundsWon);
        Assert.Equal(1, match.RoundsLost);
        Assert.Equal(1, match.Encounters);
        Assert.Equal(AimCategory.Hasty, match.MainHabit);
        Assert.True(match.IsCurrentVersion);
    }

    [Fact]
    public async Task Profiles_AddIsIdempotentPerSteamId()
    {
        var ct = TestContext.Current.CancellationToken;
        var readModel = new GloriaReadModel(_factory, "v");

        await readModel.AddProfileAsync(1, "first", ct);
        await readModel.AddProfileAsync(2, "second", ct);
        await readModel.AddProfileAsync(1, "renamed", ct);

        var profiles = await readModel.ProfilesAsync(ct);
        Assert.Equal(["renamed", "second"], profiles.Select(p => p.Name));
    }

    [Fact]
    [Trait("Category", "Demo")]
    public async Task Import_RealDemo_StoresEveryPlayerOnceAndSkipsReimport()
    {
        var path = FirstDemoOrSkip();
        var ct = TestContext.Current.CancellationToken;
        var importer = new DemoImporter(_factory, new DemoFileDemoParser(), new RuleBasedAimClassifier());

        var result = await importer.ImportAsync(path, cancellationToken: ct);
        var again = await importer.ImportAsync(path, cancellationToken: ct);

        Assert.Equal(ImportStage.Done, result.Outcome);
        Assert.Equal(ImportStage.Skipped, again.Outcome);
        Assert.True(result.Encounters > 0);
        TestContext.Current.SendDiagnosticMessage(
            $"{Path.GetFileName(path)}: {result.Encounters} encounters, traces {result.TraceBytes / 1024} kB, {result.Elapsed.TotalSeconds:0.0} s");

        await using var db = _factory.CreateDbContext();
        var players = await db.Encounters.Select(e => e.PlayerSteamId).Distinct().CountAsync(ct);
        Assert.True(players > 1, "Encounters should be stored for every player, not just one.");

        var readModel = new GloriaReadModel(_factory, importer.AnalysisVersionKey);
        var candidate = (await readModel.PlayerCandidatesAsync(ct))[0];
        var match = Assert.Single(await readModel.MatchesAsync(candidate.SteamId, ct));
        Assert.True(match.RoundsWon + match.RoundsLost > 0);

        var detail = await readModel.MatchDetailAsync(match.DemoId, candidate.SteamId, ct);
        Assert.NotNull(detail);
        var encounter = await readModel.EncounterDetailAsync(detail.Encounters[0].Id, ct);
        Assert.NotNull(encounter?.Trace);
        Assert.True(encounter.Trace.Count > 0);
    }

    private static string FirstDemoOrSkip()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Gloria.slnx")))
                continue;

            var demos = Path.Combine(dir.FullName, "_demos");
            var first = Directory.Exists(demos) ? Directory.GetFiles(demos, "*.dem").Order(StringComparer.Ordinal).FirstOrDefault() : null;
            Assert.SkipWhen(first is null, "No .dem files found in _demos/.");
            return first!;
        }

        Assert.Skip("Repository root not found.");
        return "";
    }

    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<GloriaDbContext>
    {
        public GloriaDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<GloriaDbContext>().UseSqlite(connection).Options);
    }
}
