using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Weapons;
using Gloria.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppSettings _settings;

    public SettingsTests()
    {
        _connection.Open();
        var factory = new TestDbContextFactory(_connection);
        DatabaseInitializer.Migrate(factory);
        _settings = new AppSettings(factory);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void DemoFolders_AreAddedOnceAndRemoved()
    {
        Assert.Empty(_settings.DemoFolders);

        _settings.AddDemoFolder(@"C:\demos");
        _settings.AddDemoFolder(@"D:\more|demos");
        _settings.AddDemoFolder(@"c:\DEMOS");

        Assert.Equal([@"C:\demos", @"D:\more|demos"], _settings.DemoFolders);

        _settings.RemoveDemoFolder(@"C:\Demos");

        Assert.Equal([@"D:\more|demos"], _settings.DemoFolders);
    }

    [Fact]
    public void CurrentProfile_RoundTripsAndClears()
    {
        Assert.Null(_settings.CurrentProfile);

        _settings.CurrentProfile = 76561198000000001;
        Assert.Equal(76561198000000001UL, _settings.CurrentProfile);

        _settings.CurrentProfile = null;
        Assert.Null(_settings.CurrentProfile);
        Assert.Null(_settings.Get(AppSettings.CurrentProfileKey));
    }

    [Fact]
    public void ToFeatures_MapsTheStoredMetrics()
    {
        var record = new EncounterRecord
        {
            Outcome = EncounterOutcome.Kill,
            TimeToFirstShot = 0.25,
            TimeToOnTarget = null,
            AngularErrorAtStart = 12f,
            AngularErrorAtFirstShot = 0.5f,
            ShotCount = 4,
            ShotsWhileMoving = 1,
            DistanceAtFirstShot = 900f,
            FirstShotWeapon = "weapon_ak47",
            OpponentWeapon = "glock",
            PlayerHealthAtStart = 100,
            OpponentHealthAtStart = 27,
            FiredFirst = true,
        };

        var features = record.ToFeatures();

        Assert.Equal(0.25f, features.TimeToFirstShot);
        Assert.Null(features.TimeToOnTarget);
        Assert.Equal(12f, features.AngularErrorAtFirstVisible);
        Assert.Equal(0.25f, features.ShotsWhileMovingRatio);
        Assert.Equal(WeaponClass.Rifle, features.WeaponClass);
        Assert.Equal(WeaponClass.Pistol, features.OpponentWeaponClass);
        Assert.True(features.IsKill);
        Assert.Equal(27, features.OpponentHealth);
        Assert.True(features.FiredFirst);
    }

    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<GloriaDbContext>
    {
        public GloriaDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<GloriaDbContext>().UseSqlite(connection).Options);
    }
}
