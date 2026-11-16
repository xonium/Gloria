using System.Diagnostics;
using Gloria.Core.Analysis;
using Gloria.Core.Classification;
using Gloria.Core.Ingestion;
using Gloria.Data.Corrections;
using Gloria.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data.Import;

public enum ImportStage
{
    Queued,
    Hashing,
    Parsing,
    Analyzing,
    Saving,
    Done,
    Skipped,
    Failed,
}

/// <param name="Fraction">Progress within <paramref name="Stage"/>, 0..1. Only parsing reports more than start and end.</param>
public readonly record struct ImportProgress(ImportStage Stage, double Fraction);

/// <param name="Outcome"><see cref="ImportStage.Done"/>, <see cref="ImportStage.Skipped"/> or <see cref="ImportStage.Failed"/>.</param>
/// <param name="TraceBytes">Total size of the stored encounter traces.</param>
public sealed record ImportResult(ImportStage Outcome, int? DemoId, string? Message, int Encounters, long TraceBytes, TimeSpan Elapsed);

/// <summary>
/// Imports one demo: hashes it, skips it if already imported, then parses and analyzes it once for every
/// player and stores the results. Nothing parsed is cached, so the file is not needed afterwards.
/// CPU-bound: call it off the UI thread.
/// </summary>
public sealed class DemoImporter(
    IDbContextFactory<GloriaDbContext> dbFactory,
    IDemoParser parser,
    IAimClassifier classifier,
    DemoAnalyzer? analyzer = null)
{
    private readonly DemoAnalyzer _analyzer = analyzer ?? new DemoAnalyzer();

    /// <summary>Parser and analysis versions of what this importer stores; demos stored under another key are older.</summary>
    public string AnalysisVersionKey => _analyzer.VersionKeyFor(parser);

    public async Task<ImportResult> ImportAsync(string path, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            progress?.Report(new ImportProgress(ImportStage.Hashing, 0));
            string hash;
            await using (var stream = File.OpenRead(path))
                hash = await DemoHasher.ComputeSha256Async(stream, cancellationToken);

            CorrectionLookup corrections;
            DateTimeOffset? userDate;
            await using (var db = dbFactory.CreateDbContext())
            {
                var existing = await db.Demos
                    .Where(d => d.ContentHash == hash)
                    .Select(d => new { d.Id, d.FileName })
                    .FirstOrDefaultAsync(cancellationToken);
                if (existing is not null)
                    return new ImportResult(ImportStage.Skipped, existing.Id, $"Already imported as {existing.FileName}.", 0, 0, stopwatch.Elapsed);

                // A demo imported again after re-analysis keeps the user's corrections.
                corrections = new CorrectionLookup(await db.Corrections.AsNoTracking().Where(c => c.DemoHash == hash).ToListAsync(cancellationToken));
                userDate = (await db.PlayedAtOverrides.AsNoTracking().FirstOrDefaultAsync(d => d.ContentHash == hash, cancellationToken))?.PlayedAt;
            }

            progress?.Report(new ImportProgress(ImportStage.Parsing, 0));
            ParsedDemo demo;
            await using (var stream = File.OpenRead(path))
            {
                demo = await parser.ParseAsync(
                    stream,
                    new ImmediateProgress(p => progress?.Report(new ImportProgress(ImportStage.Parsing, p))),
                    cancellationToken);
            }

            progress?.Report(new ImportProgress(ImportStage.Analyzing, 0));
            var analysis = _analyzer.Analyze(demo);
            var source = classifier.Source;
            var classifications = analysis.Encounters.Count > 0
                ? classifier.Classify(analysis.Encounters.Select(e => e.Features).ToList())
                : [];
            var record = BuildRecord(path, hash, demo, analysis, classifications, source, corrections);
            if (userDate is { } date)
            {
                record.PlayedAt = date;
                record.PlayedAtSource = PlayedAtSource.User;
            }


            progress?.Report(new ImportProgress(ImportStage.Saving, 0));
            await using (var db = dbFactory.CreateDbContext())
            {
                db.Demos.Add(record);
                await db.SaveChangesAsync(cancellationToken);
            }

            var traceBytes = record.Encounters.Sum(e => (long)(e.Trace?.Length ?? 0));
            return new ImportResult(ImportStage.Done, record.Id, null, record.Encounters.Count, traceBytes, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ImportResult(ImportStage.Failed, null, ex.Message, 0, 0, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Deletes demos analyzed by another version whose file is still where it was imported from, and returns
    /// those paths to import again. Demos whose file is gone keep their older results.
    /// </summary>
    public async Task<IReadOnlyList<string>> RemoveOutdatedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var outdated = await db.Demos
            .Where(d => d.AnalysisVersionKey != AnalysisVersionKey && d.FilePath != null)
            .ToListAsync(cancellationToken);
        var available = outdated.Where(d => File.Exists(d.FilePath)).ToList();
        db.Demos.RemoveRange(available);
        await db.SaveChangesAsync(cancellationToken);
        return available.Select(d => d.FilePath!).ToList();
    }

    private DemoRecord BuildRecord(
        string path,
        string hash,
        ParsedDemo demo,
        DemoAnalysis analysis,
        IReadOnlyList<AimClassification> classifications,
        string source,
        CorrectionLookup corrections)
    {
        var file = new FileInfo(path);
        var (playedAt, playedAtSource) = DemoDate.Find(path);
        var record = new DemoRecord
        {
            ContentHash = hash,
            FileName = file.Name,
            FilePath = file.FullName,
            FileSize = file.Length,
            MapName = demo.MapName,
            PlayedAt = playedAt,
            PlayedAtSource = playedAtSource,
            ImportedAt = DateTimeOffset.UtcNow,
            TickRate = demo.TickRate,
            ParserVersion = parser.Version,
            AnalysisVersionKey = AnalysisVersionKey,
        };

        foreach (var player in demo.Players.DistinctBy(p => p.SteamId))
        {
            record.Players.Add(new DemoPlayerRecord
            {
                SteamId = player.SteamId,
                Name = player.Name,
                TeamsByRound = new string(demo.Rounds.Select(r => SideAt(demo, player.SteamId, r)).ToArray()),
            });
        }

        foreach (var round in demo.Rounds)
        {
            record.Rounds.Add(new RoundRecord
            {
                Number = round.Number,
                StartTick = round.StartTick,
                FreezeEndTick = round.FreezeEndTick,
                EndTick = round.EndTick,
                Winner = round.Winner,
            });
        }

        for (var i = 0; i < analysis.Encounters.Count; i++)
        {
            var (encounter, m, _, labels) = analysis.Encounters[i];
            var classification = classifications[i];
            var trace = EncounterTraceBuilder.Build(demo, encounter);
            record.Encounters.Add(new EncounterRecord
            {
                PlayerSteamId = encounter.PlayerSteamId,
                OpponentSteamId = encounter.OpponentSteamId,
                RoundNumber = encounter.RoundNumber,
                StartTick = encounter.StartTick,
                EndTick = encounter.EndTick,
                Outcome = encounter.Outcome,
                Approach = m.Approach,
                TimeToFirstShot = m.TimeToFirstShot,
                TimeToOnTarget = m.TimeToOnTarget,
                FirstShotTick = m.FirstShotTick,
                FirstShotWeapon = m.FirstShotWeapon,
                OpponentWeapon = m.OpponentWeapon,
                ShotCount = m.ShotTicks.Count,
                ShotsWhileMoving = m.ShotsWhileMoving,
                SpeedAtFirstShot = m.SpeedAtShots.Count > 0 ? m.SpeedAtShots[0] : 0,
                SpeedRatioAtFirstShot = m.SpeedRatioAtFirstShot,
                AngularErrorAtStart = m.AngularErrorAtStart,
                AngularErrorAtFirstShot = m.AngularErrorAtFirstShot,
                PeakOvershootDegrees = m.PeakOvershootDegrees,
                CorrectionCount = m.CorrectionCount,
                FlickDistanceDegrees = m.FlickDistanceDegrees,
                ApproachStartTick = m.ApproachStartTick,
                PreAimErrorDegrees = m.PreAimErrorDegrees,
                DistanceAtFirstShot = m.DistanceAtFirstShot,
                PlayerHealthAtStart = m.PlayerHealthAtStart,
                OpponentHealthAtStart = m.OpponentHealthAtStart,
                FiredFirst = m.FiredFirst,
                FirstShotHit = labels.FirstShotHit,
                WonDuel = labels.WonDuel,
                Trace = trace is null ? null : TraceCodec.Encode(trace),
            });
            record.Encounters[^1].ApplyClassification(classification, source, corrections.For(record.Encounters[^1]));
        }

        return record;
    }

    private static char SideAt(ParsedDemo demo, ulong steamId, RoundInfo round)
    {
        foreach (var tick in new[] { round.FreezeEndTick, round.FreezeEndTick + demo.TickRate, round.EndTick })
        {
            if (demo.Timeline.TryGet(steamId, tick, out var state))
            {
                return state.Team switch
                {
                    Team.Terrorist => 'T',
                    Team.CounterTerrorist => 'C',
                    _ => '-',
                };
            }
        }

        return '-';
    }

    /// <summary>Forwards progress on the reporting thread, unlike <see cref="Progress{T}"/>, which posts to a captured context.</summary>
    private sealed class ImmediateProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
