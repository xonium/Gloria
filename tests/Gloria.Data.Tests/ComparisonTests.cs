using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Weapons;
using Gloria.Data.Entities;
using Gloria.Data.Queries;
using Gloria.Data.References;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data.Tests;

public sealed class ComparisonTests : IDisposable
{
    private const ulong Me = 76561198000000001;
    private const ulong Friend = 76561198000000002;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestDbContextFactory _factory;

    public ComparisonTests()
    {
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
        DatabaseInitializer.Migrate(_factory);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ComparisonSide_CountsOnlyThatPlayersEncountersInTheirLatestDemos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _factory.CreateDbContext())
        {
            // Oldest first: both players in each demo, the friend only in the last two.
            for (var i = 0; i < 3; i++)
            {
                var demo = new DemoRecord
                {
                    ContentHash = $"h{i}",
                    FileName = $"{i}.dem",
                    MapName = "de_mirage",
                    AnalysisVersionKey = "v",
                    PlayedAt = new DateTimeOffset(2026, 10, 1 + i, 20, 0, 0, TimeSpan.Zero),
                };
                demo.Players.Add(new DemoPlayerRecord { SteamId = Me, Name = "me", TeamsByRound = "C" });
                demo.Encounters.Add(Encounter(Me, EncounterOutcome.Kill, 0.2 + i * 0.1, "ak47"));
                demo.Encounters.Add(Encounter(Me, EncounterOutcome.Death, 0.3 + i * 0.1, "glock"));
                if (i > 0)
                {
                    demo.Players.Add(new DemoPlayerRecord { SteamId = Friend, Name = "friend", TeamsByRound = "T" });
                    demo.Encounters.Add(Encounter(Friend, EncounterOutcome.Kill, 0.5, "ak47"));
                }

                db.Demos.Add(demo);
            }

            // A demo from an older analysis is left out.
            var old = new DemoRecord { ContentHash = "old", FileName = "old.dem", MapName = "de_nuke", AnalysisVersionKey = "old" };
            old.Players.Add(new DemoPlayerRecord { SteamId = Me, Name = "me", TeamsByRound = "C" });
            old.Encounters.Add(Encounter(Me, EncounterOutcome.Kill, 9, "ak47"));
            db.Demos.Add(old);
            await db.SaveChangesAsync(ct);
        }

        var readModel = new GloriaReadModel(_factory, "v");
        var me = await readModel.ComparisonSideAsync(Me, "me", ComparisonKind.You, lastDemos: 2, ct);
        var friend = await readModel.ComparisonSideAsync(Friend, "friend", ComparisonKind.Friend, ct: ct);

        Assert.Equal(2, me.Demos);
        Assert.Equal(4, me.Overall.Encounters);
        Assert.Equal(2, me.Overall.Kills);
        Assert.All(me.Encounters, e => Assert.Equal(Me, e.PlayerSteamId));
        // Pooled over the two latest demos: 0.3, 0.4, 0.4, 0.5; the upper middle one.
        Assert.Equal(0.4, me.Overall.MedianTimeToFirstShot!.Value, 6);
        Assert.Equal(2, me.StatsFor(WeaponClass.Rifle).Encounters);
        Assert.Equal(2, me.StatsFor(WeaponClass.Pistol).Deaths);
        Assert.Equal(2, me.LossesWith(null).Count);
        Assert.Empty(me.LossesWith(WeaponClass.Rifle));

        Assert.Equal(2, friend.Demos);
        Assert.Equal(2, friend.Overall.Encounters);
        Assert.Equal(0, friend.Overall.Deaths);
    }

    [Fact]
    public async Task Tally_OfRecordsMatchesTheReadModelsPerDemoStats()
    {
        var ct = TestContext.Current.CancellationToken;
        var encounters = new[]
        {
            Encounter(Me, EncounterOutcome.Kill, 0.25, "ak47", hit: true, flags: 1 << (int)AimCategory.Overshoot, preAim: 3f, ttk: 0.4),
            Encounter(Me, EncounterOutcome.Kill, 0.3, "ak47", ttk: 0.1, opponentHealth: 40),
            Encounter(Me, EncounterOutcome.Death, 0.5, "ak47", hit: false, flags: 1 << (int)AimCategory.MediumFlick, speedRatio: 1.4f, preAim: 20f),
            Encounter(Me, EncounterOutcome.Disengage, null, "deagle"),
        };
        await using (var db = _factory.CreateDbContext())
        {
            var demo = new DemoRecord { ContentHash = "t", FileName = "t.dem", MapName = "de_dust2", AnalysisVersionKey = "v" };
            demo.Players.Add(new DemoPlayerRecord { SteamId = Me, Name = "me", TeamsByRound = "C" });
            foreach (var e in encounters)
                demo.Encounters.Add(e);
            db.Demos.Add(demo);
            await db.SaveChangesAsync(ct);
        }

        var stats = GloriaReadModel.Tally(encounters);
        var demoStats = Assert.Single((await new GloriaReadModel(_factory, "v").ProgressAsync(Me, ct: ct)).Demos);

        Assert.Equal(
            (demoStats.Encounters, demoStats.Kills, demoStats.Deaths, demoStats.FirstShotHits, demoStats.FirstShotKnown, demoStats.MedianTimeToFirstShot,
                demoStats.MovingFirstShots, demoStats.Clean, demoStats.MedianPreAimError, demoStats.PreAimed, demoStats.MedianTimeToKill, demoStats.TimeToKillKills),
            (stats.Encounters, stats.Kills, stats.Deaths, stats.FirstShotHits, stats.FirstShotKnown, stats.MedianTimeToFirstShot,
                stats.MovingFirstShots, stats.Clean, stats.MedianPreAimError, stats.PreAimed, stats.MedianTimeToKill, stats.TimeToKillKills));
        Assert.Equal(demoStats.HabitCounts, stats.HabitCounts);
        // The kill on a hurt opponent doesn't count.
        Assert.Equal(1, stats.TimeToKillKills);
        Assert.Equal(0.4, stats.MedianTimeToKill);
    }

    [Fact]
    public async Task Progress_TimeToKillPerGunOverFullHealthKills()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _factory.CreateDbContext())
        {
            // Oldest first.
            (string Weapon, double Ttk, int Health)[][] kills =
            [
                [("ak47", 0.5, 100), ("weapon_m4a1_silencer", 0.6, 100), ("ak47", 0.05, 30)],
                [],
                [("ak47", 0.3, 100), ("m4a1_silencer", 0.4, 100), ("ak47", 0.7, 100)],
            ];
            for (var i = 0; i < kills.Length; i++)
            {
                var demo = new DemoRecord
                {
                    ContentHash = $"k{i}",
                    FileName = $"k{i}.dem",
                    MapName = "de_inferno",
                    AnalysisVersionKey = "v",
                    PlayedAt = new DateTimeOffset(2026, 10, 1 + i, 20, 0, 0, TimeSpan.Zero),
                };
                demo.Players.Add(new DemoPlayerRecord { SteamId = Me, Name = "me", TeamsByRound = "C" });
                foreach (var (weapon, ttk, health) in kills[i])
                    demo.Encounters.Add(Encounter(Me, EncounterOutcome.Kill, 0.3, weapon, ttk: ttk, opponentHealth: health));
                demo.Encounters.Add(Encounter(Me, EncounterOutcome.Death, 0.3, "ak47"));
                db.Demos.Add(demo);
            }

            await db.SaveChangesAsync(ct);
        }

        var progress = await new GloriaReadModel(_factory, "v").ProgressAsync(Me, ct: ct);

        // Upper middle of an even count, as for the other medians: 0.5 and 0.6 → 0.6.
        Assert.Equal([0.6, null, 0.4], progress.Demos.Select(d => d.MedianTimeToKill));
        Assert.Equal([2, 0, 3], progress.Demos.Select(d => d.TimeToKillKills));
        Assert.Equal(["ak47", "m4a1_silencer"], progress.Guns.Select(g => g.Weapon));
        var ak = progress.Guns[0];
        Assert.Equal(3, ak.Kills);
        Assert.Equal(0.5, ak.Median);
        Assert.Equal([[0.5], [], [0.3, 0.7]], ak.SamplesPerDemo.Select(s => s.ToArray()));
        Assert.Equal(2, progress.Guns[1].Kills);
    }

    [Fact]
    public void Gaps_TimeToKillIsLowerBetterAndDependsOnOpposition()
    {
        var you = Stats(kills: 4, deaths: 6, firstShotHits: 2, known: 10, time: 0.6, moving: 5, encounters: 10, ttk: 0.5);
        var them = Stats(kills: 8, deaths: 2, firstShotHits: 5, known: 10, time: 0.3, moving: 1, encounters: 10, ttk: 0.4);

        var gap = Comparison.Gaps(you, them).Single(g => g.Name == "Median time to kill");

        Assert.True(gap.Gap > 0);
        Assert.True(gap.DependsOnOpposition);
        Assert.DoesNotContain(Comparison.Gaps(you, Stats(1, 0, 0, 0, null, 0, 1)), g => g.Name == "Median time to kill");
    }

    [Fact]
    public void Gaps_ArePositiveWhereYouAreWorseWhicheverWayIsBetter()
    {
        var you = Stats(kills: 4, deaths: 6, firstShotHits: 2, known: 10, time: 0.6, moving: 5, encounters: 10);
        var them = Stats(kills: 8, deaths: 2, firstShotHits: 5, known: 10, time: 0.3, moving: 1, encounters: 10);

        var gaps = Comparison.Gaps(you, them).ToDictionary(g => g.Name);

        Assert.Equal(0.4, gaps["Duels won"].Gap, 6);
        Assert.Equal(0.3, gaps["First-shot hit"].Gap, 6);
        Assert.Equal(0.4, gaps["Moving first shots"].Gap, 6);
        // Twice as slow: the relative difference counts up to 100%.
        Assert.Equal(Comparison.RelativeWeight, gaps["Median time to first shot"].Gap, 6);
        Assert.True(gaps["Duels won"].DependsOnOpposition);
        Assert.False(gaps["Moving first shots"].DependsOnOpposition);

        var reversed = Comparison.Gaps(them, you).ToDictionary(g => g.Name);
        Assert.True(reversed["Duels won"].Gap < 0);
        Assert.True(reversed["Moving first shots"].Gap < 0);
    }

    [Fact]
    public void Gaps_LeaveOutMetricsEitherPlayerHasNoValueFor()
    {
        var you = Stats(kills: 1, deaths: 1, firstShotHits: 0, known: 0, time: null, moving: 0, encounters: 2);
        var them = Stats(kills: 2, deaths: 0, firstShotHits: 1, known: 2, time: 0.4, moving: 0, encounters: 2);

        var names = Comparison.Gaps(you, them).Select(g => g.Name).ToList();

        Assert.Contains("Duels won", names);
        Assert.DoesNotContain("First-shot hit", names);
        Assert.DoesNotContain("Median time to first shot", names);
        Assert.DoesNotContain("Median pre-aim error", names);
        Assert.DoesNotContain(names, n => n == nameof(AimCategory.MovingShot));
        Assert.Contains(nameof(AimCategory.Hasty), names);
    }

    [Fact]
    public void ReferencePlayer_RoundTripsTheFeaturesTheModelsUse()
    {
        var records = new[]
        {
            Encounter(Me, EncounterOutcome.Kill, 0.25, "m4a1_silencer", hit: true, speedRatio: 0.3f, preAim: 2.5f, ttk: 0.45),
            Encounter(Me, EncounterOutcome.Death, null, "glock", hit: null, speedRatio: 1.7f, preAim: null),
        };
        records[0].TimeToOnTarget = 0.2;
        records[0].OpponentWeapon = "ak47";
        records[0].ShotCount = 4;
        records[0].ShotsWhileMoving = 1;
        records[0].PeakOvershootDegrees = 1.5f;
        records[0].CorrectionCount = 2;
        records[0].DistanceAtFirstShot = 1200;
        records[0].PlayerHealthAtStart = 74;
        records[0].FiredFirst = true;
        var player = new ReferencePlayer(
            ReferencePlayer.CurrentFormat,
            "pro",
            Me,
            "2 demos",
            "v",
            DateTimeOffset.UnixEpoch,
            [new ReferenceDemo("de_mirage", 24)],
            records.Select(r => ReferenceEncounter.From(r, 0)).ToList());

        using var stream = new MemoryStream();
        player.Write(stream);
        stream.Position = 0;
        var read = ReferencePlayer.Read(stream);
        var restored = read.ToRecords();

        Assert.Equal("pro", read.Name);
        Assert.Equal(Me, read.SteamId);
        Assert.Equal(records.Select(r => r.ToFeatures()), restored.Select(r => r.ToFeatures()));
        Assert.Equal(records.Select(r => (r.Outcome, r.FirstShotHit, r.WonDuel)), restored.Select(r => (r.Outcome, r.FirstShotHit, r.WonDuel)));
        Assert.Equal([0.45, null], restored.Select(r => r.TimeToKill));
        Assert.Equal(2, restored.Select(r => r.Id).Distinct().Count());
    }

    private static EncounterRecord Encounter(
        ulong player,
        EncounterOutcome outcome,
        double? time,
        string weapon,
        bool? hit = null,
        int flags = 0,
        float speedRatio = 0.5f,
        float? preAim = 4f,
        double? ttk = null,
        int opponentHealth = TimeToKill.FullHealth) => new()
    {
        PlayerSteamId = player,
        RoundNumber = 1,
        Outcome = outcome,
        TimeToFirstShot = time,
        FirstShotWeapon = weapon,
        FirstShotHit = hit,
        WonDuel = outcome switch { EncounterOutcome.Kill => true, EncounterOutcome.Death => false, _ => null },
        Flags = flags,
        SpeedRatioAtFirstShot = speedRatio,
        PreAimErrorDegrees = preAim,
        TimeToKill = ttk,
        OpponentHealthAtStart = opponentHealth,
    };

    private static EncounterStats Stats(int kills, int deaths, int firstShotHits, int known, double? time, int moving, int encounters, double? ttk = null) =>
        new(encounters, kills, deaths, firstShotHits, known, time, moving, new int[Enum.GetValues<AimCategory>().Length], encounters, null, encounters, ttk, ttk is null ? 0 : kills);

    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<GloriaDbContext>
    {
        public GloriaDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<GloriaDbContext>().UseSqlite(connection).Options);
    }
}
