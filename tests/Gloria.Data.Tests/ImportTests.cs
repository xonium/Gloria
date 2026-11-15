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
            [new TraceShot(1001, true, 2, 0f), new TraceShot(1001, false, null, 1.5f), new TraceShot(1001, false, null)],
            [2.5f, -1.25f],
            [3f, 0.75f]);

        var decoded = TraceCodec.Decode(TraceCodec.Encode(trace));

        Assert.NotNull(decoded);
        Assert.Equal(1000, decoded.StartTick);
        Assert.Equal(64, decoded.TickRate);
        Assert.Equal(trace.ErrorX, decoded.ErrorX);
        Assert.Equal(trace.Speed, decoded.Speed);
        Assert.Equal(trace.Shots, decoded.Shots);
        Assert.True(decoded.HasBullets);
        Assert.Equal(trace.BulletX, decoded.BulletX);
        Assert.Equal(trace.BulletY, decoded.BulletY);
    }

    [Fact]
    public void TraceCodec_RoundTripsTheScene()
    {
        var scene = new TraceScene(
            Team.CounterTerrorist,
            [-1200.4f, -1190f], [350f, 352.6f], [11800f, 11801f], [90f, 91.5f], [100, 100],
            [-600f, -605f], [400f, 401f], [-64f, -64f], [-178f, -177.5f], [100, 62], [false, true],
            [new TraceWeapon(1000, "ak47")],
            [new TraceWeapon(1000, "m4a1_silencer"), new TraceWeapon(1001, "")],
            [new TraceDamage(1001, true, "ak47", 38, 2), new TraceDamage(1001, false, "hegrenade", 40, 0)],
            new TraceDeath(1001, true, "ak47", true));
        var trace = new EncounterTrace(
            1000, 64, [1f, 2f], [0f, 0f], [1f, 0f], [200f, 0f], [600f, 600f],
            [new TraceShot(1001, true, 2, 0f, "ak47", 38), new TraceShot(1001, false, null, 1.5f, "ak47")],
            [1f, 2f], [0f, 0f], scene);

        var decoded = TraceCodec.Decode(TraceCodec.Encode(trace))!;

        Assert.Equal(trace.Shots, decoded.Shots);
        var s = decoded.Scene!;
        Assert.Equal(Team.CounterTerrorist, s.OpponentTeam);
        Assert.Equal([-1200f, -1190f], s.PlayerX);
        Assert.Equal([11800f, 11801f], s.PlayerZ);
        Assert.Equal(scene.OpponentYaw, s.OpponentYaw);
        Assert.Equal(scene.OpponentHealth, s.OpponentHealth);
        Assert.Equal(scene.OpponentDucking, s.OpponentDucking);
        Assert.Equal(scene.PlayerWeapons, s.PlayerWeapons);
        Assert.Equal(scene.OpponentWeapons, s.OpponentWeapons);
        Assert.Equal(scene.Damage, s.Damage);
        Assert.Equal(scene.Death, s.Death);
        Assert.Null(TraceScene.WeaponAt(s.OpponentWeapons, 1001));
        Assert.Equal("m4a1_silencer", TraceScene.WeaponAt(s.OpponentWeapons, 1000));
    }

    [Fact]
    public void TraceCodec_DecodesFormat2WithoutTheScene()
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer))
        {
            writer.Write((byte)2);
            writer.Write(500);
            writer.Write((short)64);
            writer.Write(1);
            foreach (var value in new[] { 1f, 0.5f, 0.25f, 200f, 900f, 1.5f, 0.75f })
                writer.Write((Half)value);
            writer.Write((short)1);
            writer.Write(500);
            writer.Write(true);
            writer.Write((short)1);
            writer.Write((Half)0f);
        }

        var decoded = TraceCodec.Decode(buffer.ToArray())!;

        Assert.Null(decoded.Scene);
        Assert.Equal([1.5f], decoded.BulletX);
        Assert.Equal([new TraceShot(500, true, 1, 0f)], decoded.Shots);
    }

    [Fact]
    public void TraceCodec_DecodesFormat1WithoutBullets()
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer))
        {
            writer.Write((byte)1);
            writer.Write(500);
            writer.Write((short)64);
            writer.Write(2);
            foreach (var value in new[] { 1f, 2f, 0.5f, 0f, 0.25f, 0.5f, 200f, 100f, 900f, 905f })
                writer.Write((Half)value);
            writer.Write((short)1);
            writer.Write(501);
            writer.Write(true);
            writer.Write((short)1);
        }

        var decoded = TraceCodec.Decode(buffer.ToArray());

        Assert.NotNull(decoded);
        Assert.False(decoded.HasBullets);
        Assert.Equal([1f, 2f], decoded.ErrorX);
        Assert.Equal([900f, 905f], decoded.Distance);
        Assert.Equal([new TraceShot(501, true, 1)], decoded.Shots);
    }

    [Fact]
    public void TraceCodec_RefusesUnknownFormats() => Assert.Null(TraceCodec.Decode([99, 0, 0, 0]));

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
    public async Task DemoDates_SetKeepsAnOverrideAndClearingRestoresTheFileDate()
    {
        var ct = TestContext.Current.CancellationToken;
        var file = Path.Combine(Directory.CreateTempSubdirectory("gloria-date-").FullName, "a.dem");
        await File.WriteAllBytesAsync(file, [1, 2, 3], ct);
        var written = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, written);
        File.SetCreationTimeUtc(file, written);
        int id;
        await using (var db = _factory.CreateDbContext())
        {
            var demo = new DemoRecord { ContentHash = "dd", FileName = "a.dem", FilePath = file, PlayedAt = written, PlayedAtSource = PlayedAtSource.FileTime };
            db.Demos.Add(demo);
            await db.SaveChangesAsync(ct);
            id = demo.Id;
        }

        var dates = new DemoDates(_factory);
        var played = new DateTimeOffset(2026, 8, 20, 21, 30, 0, TimeSpan.FromHours(2));

        var set = await dates.SetAsync(id, played, ct);
        Assert.Equal(played, set?.PlayedAt);
        Assert.Equal(PlayedAtSource.User, set?.Source);
        await using (var db = _factory.CreateDbContext())
            Assert.Equal(played, (await db.PlayedAtOverrides.SingleAsync(ct)).PlayedAt);

        var cleared = await dates.SetAsync(id, null, ct);
        Assert.Equal(new DateTimeOffset(written), cleared?.PlayedAt);
        Assert.Equal(PlayedAtSource.FileTime, cleared?.Source);
        await using (var db = _factory.CreateDbContext())
            Assert.Empty(db.PlayedAtOverrides);

        File.Delete(file);
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

        // A date the user set before re-analysis is applied to the imported demo.
        string hash;
        await using (var stream = File.OpenRead(path))
            hash = await DemoHasher.ComputeSha256Async(stream, ct);
        var userDate = new DateTimeOffset(2026, 3, 4, 18, 0, 0, TimeSpan.Zero);
        await using (var setup = _factory.CreateDbContext())
        {
            setup.PlayedAtOverrides.Add(new PlayedAtOverrideRecord { ContentHash = hash, PlayedAt = userDate });
            await setup.SaveChangesAsync(ct);
        }

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
        Assert.Equal((userDate, PlayedAtSource.User), (match.PlayedAt, match.PlayedAtSource));

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
