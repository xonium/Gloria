using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Data.Entities;
using Gloria.Data.Import;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data.Queries;

/// <summary>
/// Read-side queries for the UI. Every query that returns results takes the SteamID of the profile being
/// viewed, so one profile never sees another player's encounters.
/// </summary>
/// <param name="currentAnalysisVersion">Demos analyzed by another version are reported as older and left out of trends.</param>
public sealed class GloriaReadModel(IDbContextFactory<GloriaDbContext> dbFactory, string currentAnalysisVersion)
{
    public async Task<int> DemoCountAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Demos.CountAsync(ct);
    }

    /// <summary>Path and size of every imported file, so a folder rescan can skip them without hashing.</summary>
    public async Task<IReadOnlySet<(string Path, long Size)>> ImportedFilesAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var files = await db.Demos
            .Where(d => d.FilePath != null)
            .Select(d => new { d.FilePath, d.FileSize })
            .ToListAsync(ct);
        return files.Select(f => (f.FilePath!, f.FileSize)).ToHashSet();
    }

    public async Task<IReadOnlyList<ProfileRecord>> ProfilesAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Profiles.OrderBy(p => p.Id).ToListAsync(ct);
    }

    /// <summary>Creates the profile, or renames it if the SteamID is already tracked.</summary>
    public async Task<ProfileRecord> AddProfileAsync(ulong steamId, string name, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.SteamId == steamId, ct);
        if (profile is null)
        {
            profile = new ProfileRecord { SteamId = steamId, CreatedAt = DateTimeOffset.UtcNow };
            db.Profiles.Add(profile);
        }

        profile.Name = name;
        await db.SaveChangesAsync(ct);
        return profile;
    }

    /// <summary>Players in the imported demos, most frequent first. The one in every demo is usually the user.</summary>
    public async Task<IReadOnlyList<PlayerCandidate>> PlayerCandidatesAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.DemoPlayers
            .Where(p => p.SteamId != 0)
            .Select(p => new { p.SteamId, p.Name, p.DemoId, p.Demo!.ImportedAt })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.SteamId)
            .Select(g =>
            {
                var name = g.MaxBy(r => r.ImportedAt)!.Name;
                var others = g.Select(r => r.Name).Distinct().Where(n => n != name).ToList();
                return new PlayerCandidate(g.Key, name, g.Select(r => r.DemoId).Distinct().Count(), others);
            })
            .OrderByDescending(c => c.DemoCount)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The player's matches, newest first.</summary>
    public async Task<IReadOnlyList<MatchSummary>> MatchesAsync(ulong steamId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var data = await LoadAsync(db, steamId, demoId: null, ct);
        return data.Demos
            .Select(d => Summarize(d, data))
            .OrderByDescending(m => m.PlayedAt)
            .ToList();
    }

    public async Task<MatchDetail?> MatchDetailAsync(int demoId, ulong steamId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var data = await LoadAsync(db, steamId, demoId, ct);
        if (data.Demos.Count == 0)
            return null;

        var demo = data.Demos[0];
        var facts = data.FactsOf(demo.Id);
        var rounds = data.RoundsOf(demo.Id)
            .Select((r, i) => new RoundSummary(
                r.Number,
                Side(demo.Teams, i),
                Won(demo.Teams, i, r.Winner),
                facts.Where(f => f.RoundNumber == r.Number).OrderBy(f => f.StartTick).Select(f => f.Outcome).ToList()))
            .ToList();
        var rows = facts.Select(f => Row(f, demo, data)).OrderBy(r => r.RoundNumber).ThenBy(r => r.SecondsIntoRound).ToList();
        var opponents = facts
            .GroupBy(f => f.Opponent)
            .Select(g => new OpponentSummary(
                data.NameOf(demo.Id, g.Key),
                g.Count(),
                g.Count(f => f.Outcome == EncounterOutcome.Kill),
                g.Count(f => f.Outcome == EncounterOutcome.Death)))
            .OrderByDescending(o => o.Encounters)
            .ToList();

        return new MatchDetail(Summarize(demo, data), demo.TickRate, demo.AnalysisVersionKey, rounds, rows, opponents);
    }

    public async Task<EncounterDetail?> EncounterDetailAsync(int encounterId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var record = await db.Encounters.AsNoTracking().FirstOrDefaultAsync(e => e.Id == encounterId, ct);
        if (record is null)
            return null;

        var demo = await db.Demos.AsNoTracking()
            .Where(d => d.Id == record.DemoId)
            .Select(d => new { d.MapName, d.PlayedAt, d.TickRate })
            .SingleAsync(ct);
        var opponent = await db.DemoPlayers
            .Where(p => p.DemoId == record.DemoId && p.SteamId == record.OpponentSteamId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct);
        var freezeEnd = await db.Rounds
            .Where(r => r.DemoId == record.DemoId && r.Number == record.RoundNumber)
            .Select(r => (int?)r.FreezeEndTick)
            .FirstOrDefaultAsync(ct);
        var siblings = await db.Encounters
            .Where(e => e.DemoId == record.DemoId && e.PlayerSteamId == record.PlayerSteamId)
            .OrderBy(e => e.StartTick)
            .Select(e => e.Id)
            .ToListAsync(ct);
        var index = siblings.IndexOf(record.Id);

        return new EncounterDetail(
            record,
            demo.MapName,
            demo.PlayedAt,
            opponent ?? record.OpponentSteamId.ToString(),
            demo.TickRate,
            (record.StartTick - (freezeEnd ?? record.StartTick)) / (double)Math.Max(demo.TickRate, 1),
            TraceCodec.Decode(record.Trace),
            index > 0 ? siblings[index - 1] : null,
            index >= 0 && index < siblings.Count - 1 ? siblings[index + 1] : null);
    }

    /// <summary>Headline numbers for the last <paramref name="window"/> demos, compared with the window before.</summary>
    public async Task<OverviewData> OverviewAsync(ulong steamId, int window = 10, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var data = await LoadAsync(db, steamId, demoId: null, ct);
        var newestFirst = data.Demos.OrderByDescending(d => d.PlayedAt).ToList();
        var current = newestFirst.Where(d => d.AnalysisVersionKey == currentAnalysisVersion).ToList();

        DemoStats Stats(DemoInfo d)
        {
            var facts = data.FactsOf(d.Id);
            var times = facts.Where(f => f.TimeToFirstShot is not null).Select(f => f.TimeToFirstShot!.Value).Order().ToList();
            var habits = new int[Enum.GetValues<AimCategory>().Length];
            foreach (var f in facts)
            {
                foreach (var c in Enum.GetValues<AimCategory>())
                {
                    if ((f.Flags & (1 << (int)c)) != 0)
                        habits[(int)c]++;
                }
            }

            return new DemoStats(
                d.Id,
                d.MapName,
                d.PlayedAt,
                facts.Count,
                facts.Count(f => f.Outcome == EncounterOutcome.Kill),
                facts.Count(f => f.Outcome == EncounterOutcome.Death),
                facts.Count(f => f.FirstShotHit == true),
                facts.Count(f => f.FirstShotHit is not null),
                times.Count == 0 ? null : times[times.Count / 2],
                facts.Count(f => f.SpeedRatio > 1f),
                habits);
        }

        var recent = current.Take(window).Reverse().Select(Stats).ToList();
        var previous = current.Skip(window).Take(window).Reverse().Select(Stats).ToList();
        var worth = current.Take(3)
            .SelectMany(d => data.FactsOf(d.Id).Select(f => Row(f, d, data)))
            .Where(r => r.Flags != 0)
            .OrderByDescending(r => r.HabitScore)
            .ThenBy(r => r.Outcome == EncounterOutcome.Death ? 0 : 1)
            .Take(3)
            .ToList();

        return new OverviewData(
            data.Demos.Count,
            recent,
            previous,
            newestFirst.Take(5).Select(d => Summarize(d, data)).ToList(),
            worth,
            data.Demos.Count - current.Count);
    }

    private static async Task<PlayerData> LoadAsync(GloriaDbContext db, ulong steamId, int? demoId, CancellationToken ct)
    {
        var demos = await db.DemoPlayers
            .Where(p => p.SteamId == steamId && (demoId == null || p.DemoId == demoId))
            .Select(p => new DemoInfo(
                p.DemoId,
                p.Demo!.MapName,
                p.Demo.FileName,
                p.Demo.PlayedAt,
                p.Demo.TickRate,
                p.Demo.AnalysisVersionKey,
                p.Demo.ContentHash,
                p.TeamsByRound))
            .ToListAsync(ct);
        var ids = demos.Select(d => d.Id).ToList();

        var rounds = await db.Rounds
            .Where(r => ids.Contains(r.DemoId))
            .OrderBy(r => r.DemoId).ThenBy(r => r.Number)
            .Select(r => new RoundInfoRow(r.DemoId, r.Number, r.FreezeEndTick, r.Winner))
            .ToListAsync(ct);

        var facts = await db.Encounters
            .Where(e => e.PlayerSteamId == steamId && ids.Contains(e.DemoId))
            .Select(e => new Facts(
                e.Id,
                e.DemoId,
                e.RoundNumber,
                e.OpponentSteamId,
                e.StartTick,
                e.EndTick,
                e.Outcome,
                e.TimeToFirstShot,
                e.FirstShotHit,
                e.SpeedRatioAtFirstShot,
                e.AngularErrorAtFirstShot,
                e.FirstShotWeapon,
                e.OpponentWeapon,
                e.Flags,
                e.OvershootProbability,
                e.UndershootProbability,
                e.MovingShotProbability,
                e.HastyProbability,
                e.SlowReactionProbability))
            .ToListAsync(ct);

        var names = await db.DemoPlayers
            .Where(p => ids.Contains(p.DemoId))
            .Select(p => new { p.DemoId, p.SteamId, p.Name })
            .ToListAsync(ct);

        return new PlayerData(
            demos,
            rounds.ToLookup(r => r.DemoId),
            facts.ToLookup(f => f.DemoId),
            names.ToDictionary(n => (n.DemoId, n.SteamId), n => n.Name));
    }

    private MatchSummary Summarize(DemoInfo d, PlayerData data)
    {
        var rounds = data.RoundsOf(d.Id);
        var won = 0;
        var lost = 0;
        for (var i = 0; i < rounds.Count; i++)
        {
            switch (Won(d.Teams, i, rounds[i].Winner))
            {
                case true: won++; break;
                case false: lost++; break;
            }
        }

        var facts = data.FactsOf(d.Id);
        var habit = Enum.GetValues<AimCategory>()
            .Select(c => (Category: c, Count: facts.Count(f => (f.Flags & (1 << (int)c)) != 0)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .Select(x => (AimCategory?)x.Category)
            .FirstOrDefault();

        return new MatchSummary(
            d.Id,
            d.MapName,
            d.FileName,
            d.PlayedAt,
            won,
            lost,
            Side(d.Teams, 0),
            facts.Count,
            facts.Count(f => f.Outcome == EncounterOutcome.Kill),
            facts.Count(f => f.Outcome == EncounterOutcome.Death),
            facts.Count(f => f.FirstShotHit == true),
            facts.Count(f => f.FirstShotHit is not null),
            habit,
            d.AnalysisVersionKey == currentAnalysisVersion,
            d.ContentHash);
    }

    private static EncounterRow Row(Facts f, DemoInfo demo, PlayerData data)
    {
        var freezeEnd = data.RoundsOf(demo.Id).FirstOrDefault(r => r.Number == f.RoundNumber)?.FreezeEndTick ?? f.StartTick;
        var rate = Math.Max(demo.TickRate, 1);
        float[] probabilities = [f.POvershoot, f.PUndershoot, f.PMovingShot, f.PHasty, f.PSlowReaction];
        var score = Enum.GetValues<AimCategory>()
            .Where(c => (f.Flags & (1 << (int)c)) != 0)
            .Sum(c => probabilities[(int)c]);

        return new EncounterRow(
            f.Id,
            demo.Id,
            demo.MapName,
            f.RoundNumber,
            (f.StartTick - freezeEnd) / (double)rate,
            data.NameOf(demo.Id, f.Opponent),
            f.Weapon,
            f.OpponentWeapon,
            f.Outcome,
            (f.EndTick - f.StartTick) / (double)rate,
            f.TimeToFirstShot,
            f.ErrorAtFirstShot,
            f.SpeedRatio,
            f.Flags,
            score);
    }

    private static char Side(string teams, int round) => round < teams.Length ? teams[round] : '-';

    private static bool? Won(string teams, int round, Team winner) => Side(teams, round) switch
    {
        'T' => winner == Team.Terrorist,
        'C' => winner == Team.CounterTerrorist,
        _ => null,
    };

    private sealed record DemoInfo(
        int Id,
        string MapName,
        string FileName,
        DateTimeOffset? PlayedAt,
        int TickRate,
        string AnalysisVersionKey,
        string ContentHash,
        string Teams);

    private sealed record RoundInfoRow(int DemoId, int Number, int FreezeEndTick, Team Winner);

    private sealed record Facts(
        int Id,
        int DemoId,
        int RoundNumber,
        ulong Opponent,
        int StartTick,
        int EndTick,
        EncounterOutcome Outcome,
        double? TimeToFirstShot,
        bool? FirstShotHit,
        float SpeedRatio,
        float ErrorAtFirstShot,
        string Weapon,
        string OpponentWeapon,
        int Flags,
        float POvershoot,
        float PUndershoot,
        float PMovingShot,
        float PHasty,
        float PSlowReaction);

    private sealed record PlayerData(
        IReadOnlyList<DemoInfo> Demos,
        ILookup<int, RoundInfoRow> Rounds,
        ILookup<int, Facts> Facts,
        IReadOnlyDictionary<(int DemoId, ulong SteamId), string> Names)
    {
        public IReadOnlyList<RoundInfoRow> RoundsOf(int demoId) => Rounds[demoId].ToList();

        public IReadOnlyList<Facts> FactsOf(int demoId) => Facts[demoId].ToList();

        public string NameOf(int demoId, ulong steamId) =>
            Names.TryGetValue((demoId, steamId), out var name) ? name : steamId.ToString();
    }
}
