using DemoFile;
using DemoFile.Game.Cs;

namespace Gloria.Core.Ingestion;

/// <summary><see cref="IDemoParser"/> built on the managed DemoFile.Net parser.</summary>
public sealed class DemoFileDemoParser : IDemoParser
{
    public int Version => 1;

    public async Task<ParsedDemo> ParseAsync(Stream demo, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var parser = new CsDemoParser();
        var mapName = "";
        var players = new Dictionary<ulong, PlayerInfo>();
        var events = new List<DemoEvent>();
        // TODO: collect per-tick player state (position, velocity, view angles, weapon) on each tick.
        var ticks = new List<PlayerTickState>();

        int Tick() => parser.CurrentDemoTick.Value;

        ulong? SteamIdOf(CCSPlayerController? player)
        {
            if (player is not { SteamID: not 0 })
            {
                return null;
            }

            players.TryAdd(player.SteamID, new PlayerInfo(player.SteamID, player.PlayerName));
            return player.SteamID;
        }

        parser.DemoEvents.DemoFileHeader += header => mapName = header.MapName;
        parser.Source1GameEvents.RoundStart += _ => events.Add(new RoundStartEvent(Tick()));
        parser.Source1GameEvents.RoundEnd += e => events.Add(new RoundEndEvent(Tick(), (Team)e.Winner, e.Reason));
        parser.Source1GameEvents.WeaponFire += e =>
        {
            if (SteamIdOf(e.Player) is { } shooter)
            {
                events.Add(new WeaponFireEvent(Tick(), shooter, e.Weapon));
            }
        };
        parser.Source1GameEvents.PlayerHurt += e =>
        {
            if (SteamIdOf(e.Player) is { } victim)
            {
                events.Add(new PlayerHurtEvent(Tick(), victim, SteamIdOf(e.Attacker), e.Weapon, e.DmgHealth, e.Hitgroup));
            }
        };
        parser.Source1GameEvents.PlayerDeath += e =>
        {
            if (SteamIdOf(e.Player) is { } victim)
            {
                events.Add(new PlayerDeathEvent(Tick(), victim, SteamIdOf(e.Attacker), e.Weapon, e.Headshot));
            }
        };

        var reader = DemoFileReader.Create(parser, demo);
        if (progress is not null)
        {
            reader.OnProgress = p => progress.Report(p.ProgressRatio);
        }

        await reader.ReadAllAsync(cancellationToken);

        foreach (var player in parser.PlayersIncludingDisconnected)
        {
            SteamIdOf(player);
        }

        progress?.Report(1.0);
        return new ParsedDemo(mapName, CsDemoParser.TickRate, parser.TickCount.Value, [.. players.Values], ticks, events);
    }
}
