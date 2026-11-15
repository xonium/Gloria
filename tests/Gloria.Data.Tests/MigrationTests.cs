using Gloria.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
