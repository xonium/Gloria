using Gloria.Data.Entities;
using Gloria.Data.Queries;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data.Tests;

public sealed class SprayTests : IDisposable
{
    private const ulong Me = 76561198000000001;
    private const ulong Friend = 76561198000000002;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestDbContextFactory _factory;

    public SprayTests()
    {
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
        DatabaseInitializer.Migrate(_factory);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Spray_SumsThePlayersBulletsOverTheirLatestDemos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _factory.CreateDbContext())
        {
            // Oldest first: the "last 2" period leaves out the first demo.
            db.Demos.Add(Demo("a", 1, "v", (Me, "ak47", [(1, 10, 1, 0)])));
            db.Demos.Add(Demo("b", 2, "v",
                (Me, "ak47", [(1, 4, 2, 0), (2, 3, 1, 1), (3, 1, 0, 0)]),
                (Me, "m4a1_silencer", [(1, 1, 0, 0)]),
                (Friend, "ak47", [(1, 50, 50, 50)])));
            db.Demos.Add(Demo("c", 3, "v", (Me, "ak47", [(1, 2, 0, 0), (2, 2, 1, 1)])));
            db.Demos.Add(Demo("old", 4, "old", (Me, "ak47", [(1, 99, 0, 0)])));
            await db.SaveChangesAsync(ct);
        }

        var readModel = new GloriaReadModel(_factory, "v");
        var all = await readModel.SprayAsync(Me, ct: ct);
        var recent = await readModel.SprayAsync(Me, lastDemos: 2, ct: ct);

        Assert.Equal((3, 1), (all.Demos, all.OlderVersionDemos));
        Assert.Equal(["ak47", "m4a1_silencer"], all.Guns.Select(g => g.Weapon));
        var ak = all.Gun("ak47")!;
        Assert.Equal(30, ak.Clip);
        Assert.Equal(30, ak.Bullets.Count);
        Assert.Equal((16, 5, 1), (ak.Bullets[0].Fired, ak.Bullets[1].Fired, ak.Bullets[2].Fired));
        Assert.Equal(0, ak.Bullets[3].Fired);
        Assert.Equal(20, all.Gun("m4a1_silencer")!.Bullets.Count);

        Assert.Equal(2, recent.Demos);
        Assert.Equal(6, recent.Gun("ak47")!.Sprays);
    }

    [Fact]
    public void GunSpray_ReachHitRateKillBulletAndMedianLength()
    {
        // 10 sprays: 4 of 1 bullet, 3 of 2, 2 of 3, 1 of 5.
        var gun = GunSpray.From("ak47", 30,
        [
            new SprayBullet(1, 10, 5, 0),
            new SprayBullet(2, 6, 3, 2),
            new SprayBullet(3, 3, 1, 2),
            new SprayBullet(4, 1, 1, 1),
            new SprayBullet(5, 1, 0, 0),
        ]);

        Assert.Equal(10, gun.Sprays);
        Assert.Equal([1.0, 0.6, 0.3, 0.1, 0.1, 0.0], Enumerable.Range(1, 6).Select(gun.Reach));
        Assert.Equal(0.5, gun.HitRateAt(1));
        // Too few bullets fired from the second on to show a hit rate.
        Assert.True(double.IsNaN(gun.HitRateAt(2)));
        Assert.Equal(10 / 21.0, gun.HitRate, 6);
        // Bullets 2 and 3 tie on kills: the earlier wins.
        Assert.Equal(2, gun.KillBullet);
        Assert.Equal(2, gun.MedianLength);
    }

    [Fact]
    public void GunSpray_WithoutSpraysOrKills_HasNoMedianOrKillBullet()
    {
        var gun = GunSpray.From("ak47", 30, []);

        Assert.Equal(0, gun.Sprays);
        Assert.Null(gun.MedianLength);
        Assert.Null(gun.KillBullet);
        Assert.True(double.IsNaN(gun.Reach(1)));
    }

    private static DemoRecord Demo(string hash, int day, string version, params (ulong Player, string Weapon, (int Bullet, int Fired, int Hits, int Kills)[] Bullets)[] sprays)
    {
        var demo = new DemoRecord
        {
            ContentHash = hash,
            FileName = $"{hash}.dem",
            MapName = "de_mirage",
            PlayedAt = new DateTimeOffset(2026, 11, day, 20, 0, 0, TimeSpan.Zero),
            AnalysisVersionKey = version,
        };
        foreach (var player in sprays.Select(s => s.Player).Distinct())
            demo.Players.Add(new DemoPlayerRecord { SteamId = player, Name = $"p{player % 10}", TeamsByRound = "TT" });
        foreach (var (player, weapon, bullets) in sprays)
        {
            foreach (var (bullet, fired, hits, kills) in bullets)
                demo.SprayBullets.Add(new SprayBulletRecord { SteamId = player, Weapon = weapon, Bullet = bullet, Fired = fired, Hits = hits, Kills = kills });
        }

        return demo;
    }

    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<GloriaDbContext>
    {
        public GloriaDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<GloriaDbContext>().UseSqlite(connection).Options);
    }
}
