using System.Text;
using Gloria.Core.Classification;
using Gloria.Core.Ingestion;
using Gloria.Data;
using Gloria.Data.Import;
using Gloria.Data.References;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Trainer;

/// <summary>
/// Turns one player's demos into a reference the app bundles for comparing against (src/Gloria/Resources/Raw/references).
/// The demos go through the app's own importer, into a database in memory, so the numbers are what the app would store.
/// </summary>
public static class ReferenceCommand
{
    public static async Task RunAsync(TrainerOptions options)
    {
        if (options.Player is not { Length: > 0 } player)
            throw new TrainerException("reference needs --player <SteamID64 or name>.");

        var demos = Directory.Exists(options.DemoDirectory)
            ? Directory.GetFiles(options.DemoDirectory, "*.dem").Order(StringComparer.Ordinal).ToList()
            : [];
        if (demos.Count == 0)
            throw new TrainerException($"No .dem files in {options.DemoDirectory}.");

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new MemoryDbContextFactory(connection);
        DatabaseInitializer.Migrate(factory);

        // The app classifies reference encounters with its own model when it loads them, so the rules do here.
        var importer = new DemoImporter(factory, new DemoFileDemoParser(), new RuleBasedAimClassifier());
        Console.WriteLine($"reference: {demos.Count} demos, analysis version {importer.AnalysisVersionKey}");
        foreach (var path in demos)
        {
            var result = await importer.ImportAsync(path);
            if (result.Outcome == ImportStage.Failed)
                throw new TrainerException($"{Path.GetFileName(path)}: {result.Error ?? result.Message}");

            Console.WriteLine($"  {Path.GetFileName(path)}: {result.Message ?? $"{result.Encounters} encounters, {result.Elapsed.TotalSeconds:F1} s"}");
        }

        await using var db = factory.CreateDbContext();
        var steamId = await ResolveAsync(db, player);
        var rows = await db.DemoPlayers
            .Where(p => p.SteamId == steamId)
            .Select(p => new { p.DemoId, p.Name, p.TeamsByRound, p.Demo!.MapName, p.Demo.FileName })
            .ToListAsync();
        rows = rows.OrderBy(r => r.FileName, StringComparer.Ordinal).ToList();
        var index = rows.Select((r, i) => (r.DemoId, Index: i)).ToDictionary(x => x.DemoId, x => x.Index);

        var encounters = await db.Encounters.AsNoTracking()
            .Where(e => e.PlayerSteamId == steamId)
            .OrderBy(e => e.DemoId).ThenBy(e => e.StartTick)
            .ToListAsync();

        var name = options.Name ?? rows[^1].Name;
        var maps = rows.Select(r => r.MapName.Replace("de_", "")).Distinct().ToList();
        var reference = new ReferencePlayer(
            ReferencePlayer.CurrentFormat,
            name,
            steamId,
            options.Description ?? $"{rows.Count} demos: {string.Join(", ", maps)}",
            importer.AnalysisVersionKey,
            DateTimeOffset.UtcNow,
            rows.Select(r => new ReferenceDemo(r.MapName, r.TeamsByRound.Count(c => c != '-'))).ToList(),
            encounters.Select(e => ReferenceEncounter.From(e, index[e.DemoId])).ToList());

        Directory.CreateDirectory(options.ReferenceDirectory);
        var fileName = Slug(name) + ".json";
        await using (var file = File.Create(Path.Combine(options.ReferenceDirectory, fileName)))
            reference.Write(file);

        var indexPath = Path.Combine(options.ReferenceDirectory, ReferencePlayer.IndexFileName);
        IReadOnlyList<string> files = [];
        if (File.Exists(indexPath))
        {
            await using var stream = File.OpenRead(indexPath);
            files = ReferencePlayer.ReadIndex(stream);
        }

        await using (var stream = File.Create(indexPath))
            ReferencePlayer.WriteIndex(stream, files.Append(fileName).Distinct().Order(StringComparer.Ordinal).ToList());

        Console.WriteLine($"reference: {name} ({steamId}), {rows.Count} demos, {encounters.Count} encounters -> " +
                          Path.GetRelativePath(options.RepoRoot, Path.Combine(options.ReferenceDirectory, fileName)));
    }

    private static async Task<ulong> ResolveAsync(GloriaDbContext db, string player)
    {
        if (ulong.TryParse(player, out var id))
        {
            if (!await db.DemoPlayers.AnyAsync(p => p.SteamId == id))
                throw new TrainerException($"SteamID {id} is in none of the demos.");
            return id;
        }

        var players = await db.DemoPlayers.Select(p => new { p.SteamId, p.Name }).ToListAsync();
        var matches = players
            .Where(p => string.Equals(p.Name, player, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.SteamId)
            .Distinct()
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new TrainerException($"No player named {player}. Players: {string.Join(", ", players.Select(p => p.Name).Distinct().Order())}"),
            _ => throw new TrainerException($"Several players are named {player}: {string.Join(", ", matches)}. Pass the SteamID."),
        };
    }

    /// <summary>A file name from a player name: lowercase letters and digits.</summary>
    private static string Slug(string name)
    {
        var slug = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                slug.Append(c);
        }

        return slug.Length > 0 ? slug.ToString() : "player";
    }

    private sealed class MemoryDbContextFactory(SqliteConnection connection) : IDbContextFactory<GloriaDbContext>
    {
        public GloriaDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<GloriaDbContext>().UseSqlite(connection).Options);
    }
}
