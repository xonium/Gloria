using Gloria.Core.Ingestion;
using Gloria.Core.Tests.Fixtures;

namespace Gloria.Core.Tests.Ingestion;

[Trait("Category", DemoFiles.Category)]
public class DemoFileDemoParserTests
{
    [Fact]
    public async Task ParseAsync_ReadsMapPlayersEventsAndPerTickState()
    {
        var demo = await DemoFiles.ParseFirstOrSkipAsync();
        var timeline = demo.Timeline;
        var output = TestContext.Current.TestOutputHelper!;

        Assert.False(string.IsNullOrEmpty(demo.MapName));
        Assert.True(demo.TickCount > 0);
        Assert.True(demo.Players.Count >= 10);
        Assert.Contains(demo.Events, e => e is RoundEndEvent);
        Assert.Contains(demo.Events, e => e is WeaponFireEvent);
        Assert.Contains(demo.Events, e => e is PlayerDeathEvent);

        output.WriteLine($"map {demo.MapName}, {demo.Rounds.Count} rounds, {timeline.SteamIds.Count} tracked players, " +
                         $"{timeline.RecordedTicks.Count} rows over ticks {timeline.FirstTick}..{timeline.LastTick} of {demo.TickCount}");
        Assert.True(demo.Rounds.Count >= 10, $"Only {demo.Rounds.Count} live rounds.");

        // The first live round counts, even when the demo starts inside it (parser version 5).
        var matchStart = demo.Events.OfType<MatchStartEvent>().Last().Tick;
        var firstEnd = demo.Events.OfType<RoundEndEvent>().First(e => e.Tick > matchStart).Tick;
        Assert.Equal(firstEnd, demo.Rounds[0].EndTick);
        Assert.True(timeline.SteamIds.Count >= 10);

        // Speeds: alive players rarely exceed the 250 u/s run speed by much.
        var speeds = new List<float>();
        var positions = new HashSet<(int, int)>();
        foreach (var tick in timeline.RecordedTicks.Where((_, i) => i % 16 == 0))
        {
            foreach (var id in timeline.SteamIds)
            {
                if (timeline.TryGet(id, tick, out var s) && s.IsAlive)
                {
                    speeds.Add(s.HorizontalSpeed);
                    positions.Add(((int)s.Position.X, (int)s.Position.Y));
                }
            }
        }

        speeds.Sort();
        var p99 = speeds[(int)(speeds.Count * 0.99)];
        output.WriteLine($"speed median {speeds[speeds.Count / 2]:F0}, p99 {p99:F0}, max {speeds[^1]:F0}; {positions.Count} distinct positions");
        Assert.True(positions.Count > 1000);
        Assert.True(speeds[^1] > 100, "Velocity is never above 100 u/s.");
        Assert.True(p99 <= 320, $"p99 speed {p99} is implausible.");

        // Spotted state: the killer should usually have spotted the victim shortly before a gun kill.
        Assert.True(timeline.HasSpottedData);
        var kills = demo.Events.OfType<PlayerDeathEvent>()
            .Where(d => d.AttackerSteamId is { } a && a != d.VictimSteamId && IsGun(d.Weapon))
            .ToList();
        bool SpottedWithinASecond(ulong target, ulong observer, int tick) =>
            Enumerable.Range(tick - demo.TickRate, demo.TickRate + 1).Any(t => timeline.IsSpottedBy(target, observer, t));

        var byKiller = kills.Count(k => SpottedWithinASecond(k.VictimSteamId, k.AttackerSteamId!.Value, k.Tick));
        var rate = byKiller / (double)kills.Count;

        // Baseline: an alive teammate of the killer. A wrong slot mapping makes the two rates similar.
        var teammatePairs = kills
            .SelectMany(k => timeline.SteamIds
                .Where(id => id != k.AttackerSteamId
                             && timeline.TryGet(id, k.Tick, out var s) && s.IsAlive
                             && timeline.TryGet(k.AttackerSteamId!.Value, k.Tick, out var a) && a.Team == s.Team)
                .Select(id => (k, id)))
            .ToList();
        var teammateRate = teammatePairs.Count(p => SpottedWithinASecond(p.k.VictimSteamId, p.id, p.k.Tick)) / (double)teammatePairs.Count;

        output.WriteLine($"{byKiller}/{kills.Count} gun kills had the victim spotted by the killer within 1 s ({rate:P0}); teammates {teammateRate:P0}");
        Assert.True(rate >= 0.7, $"Only {rate:P0} of kills were spotted by the killer; the spotted mask mapping is probably wrong.");
        Assert.True(rate > 3 * teammateRate, $"Killer rate {rate:P0} is not clearly above the teammate rate {teammateRate:P0}.");
    }

    private static bool IsGun(string weapon) =>
        !weapon.Contains("knife", StringComparison.OrdinalIgnoreCase)
        && !weapon.Contains("grenade", StringComparison.OrdinalIgnoreCase)
        && weapon is not ("inferno" or "molotov" or "incgrenade" or "hegrenade" or "world" or "taser" or "planted_c4");
}
