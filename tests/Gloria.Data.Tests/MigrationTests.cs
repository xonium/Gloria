using Gloria.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gloria.Data.Tests;

public sealed class MigrationTests : IDisposable
{
    // An in-memory SQLite database lives as long as its first connection stays open.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestDbContextFactory _factory;

    public MigrationTests()
    {
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Migrate_AppliesAllMigrations()
    {
        DatabaseInitializer.Migrate(_factory);

        using var db = _factory.CreateDbContext();
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    [Fact]
    public async Task Demo_RoundTripsWithPlayers()
    {
        DatabaseInitializer.Migrate(_factory);
        var ct = TestContext.Current.CancellationToken;

        await using (var db = _factory.CreateDbContext())
        {
            var demo = NewDemo("aa");
            demo.Players.Add(new DemoPlayerRecord { SteamId = 76561198000000001, Name = "player" });
            db.Demos.Add(demo);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = _factory.CreateDbContext())
        {
            var demo = await db.Demos.Include(d => d.Players).SingleAsync(ct);
            Assert.Equal("aa", demo.ContentHash);
            Assert.Equal(76561198000000001UL, Assert.Single(demo.Players).SteamId);
        }
    }

    [Fact]
    public async Task Demo_ContentHashIsUnique()
    {
        DatabaseInitializer.Migrate(_factory);
        await using var db = _factory.CreateDbContext();
        db.Demos.Add(NewDemo("same"));
        db.Demos.Add(NewDemo("same"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TimeToKill_IsFilledInForKillsAlreadyStored()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _factory.CreateDbContext())
        {
            var migrations = db.Database.GetMigrations().ToList();
            var before = migrations[migrations.FindIndex(m => m.EndsWith("_TimeToKill")) - 1];
            await db.GetService<IMigrator>().MigrateAsync(before, ct);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Demos (Id, ContentHash, FileName, FileSize, MapName, ImportedAt, TickRate, ParserVersion, AnalysisVersion, AnalysisVersionKey, PlayedAtSource)
                VALUES (1, 'aa', 'aa.dem', 0, 'de_mirage', '2026-11-01 00:00:00+00:00', 64, 6, 0, 'v', 0);
                """,
                ct);
            // A kill 32 ticks after the first shot, a death, and a kill ending before the first shot (bad data).
            foreach (var (id, outcome, first, end) in new[] { (1, 0, 1000, 1032), (2, 1, 1000, 1032), (3, 0, 1032, 1000) })
            {
                await db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO Encounters (Id, DemoId, PlayerSteamId, OpponentSteamId, RoundNumber, StartTick, EndTick, Outcome, Approach,
                        FirstShotTick, FirstShotWeapon, OpponentWeapon, ShotCount, ShotsWhileMoving, SpeedAtFirstShot, SpeedRatioAtFirstShot,
                        AngularErrorAtStart, AngularErrorAtFirstShot, PeakOvershootDegrees, CorrectionCount, FlickDistanceDegrees, ApproachStartTick,
                        DistanceAtFirstShot, PlayerHealthAtStart, OpponentHealthAtStart, FiredFirst, Flags, ClassifierSource,
                        OvershootProbability, UndershootProbability, MovingShotProbability, HastyProbability, SlowReactionProbability,
                        SmallFlickProbability, MediumFlickProbability, LargeFlickProbability)
                    VALUES ({id}, 1, 1, 2, 1, 900, {end}, {outcome}, 0, {first}, 'ak47', '', 1, 0, 0, 0, 0, 0, 0, 0, 0, 900, 0, 100, 100, 1, 0, '',
                        0, 0, 0, 0, 0, 0, 0, 0)
                    """,
                    ct);
            }
        }

        DatabaseInitializer.Migrate(_factory);

        await using (var db = _factory.CreateDbContext())
        {
            var ttk = await db.Encounters.OrderBy(e => e.Id).Select(e => e.TimeToKill).ToListAsync(ct);
            Assert.Equal([0.5, null, null], ttk);
        }
    }

    private static DemoRecord NewDemo(string hash) => new()
    {
        ContentHash = hash,
        FileName = $"{hash}.dem",
        ImportedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<GloriaDbContext>
    {
        public GloriaDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<GloriaDbContext>().UseSqlite(connection).Options);
    }
}
