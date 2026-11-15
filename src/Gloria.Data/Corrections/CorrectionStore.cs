using Gloria.Core.Classification;
using Gloria.Data.Entities;
using Gloria.Data.Queries;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data.Corrections;

/// <summary>One stored encounter as training input: its features, its demo, and the user's verdicts if any.</summary>
public sealed record TrainingRow(int EncounterId, EncounterFeatures Features, string DemoHash, IReadOnlyDictionary<AimCategory, bool>? Corrections);

/// <summary>
/// The user's corrections of aim categories, and keeping the stored flags in line with them and with the
/// current classifier.
/// </summary>
public sealed class CorrectionStore(IDbContextFactory<GloriaDbContext> dbFactory)
{
    private const int Batch = 1000;

    /// <summary>Raised after a correction was set or cleared, or stored encounters were reclassified.</summary>
    public event EventHandler? Changed;

    public async Task<IReadOnlyDictionary<AimCategory, bool>> ForEncounterAsync(int encounterId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var e = await db.Encounters.AsNoTracking()
            .Where(x => x.Id == encounterId)
            .Select(x => new { x.Demo!.ContentHash, x.PlayerSteamId, x.OpponentSteamId, x.FirstShotTick })
            .FirstOrDefaultAsync(ct);
        if (e is null)
            return new Dictionary<AimCategory, bool>();

        return await db.Corrections.AsNoTracking()
            .Where(c => c.DemoHash == e.ContentHash && c.PlayerSteamId == e.PlayerSteamId && c.OpponentSteamId == e.OpponentSteamId
                && c.FirstShotTick == e.FirstShotTick)
            .ToDictionaryAsync(c => c.Category, c => c.Value, ct);
    }

    /// <summary>
    /// Records the user's verdict on one category, or clears it when <paramref name="value"/> is null, and
    /// updates the encounter's flags. Returns the new <see cref="EncounterRecord.Flags"/> and primary category.
    /// </summary>
    public async Task<(int Flags, AimCategory? Primary)?> SetAsync(int encounterId, AimCategory category, bool? value, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var encounter = await db.Encounters.FirstOrDefaultAsync(e => e.Id == encounterId, ct);
        if (encounter is null)
            return null;

        var hash = await db.Demos.Where(d => d.Id == encounter.DemoId).Select(d => d.ContentHash).SingleAsync(ct);
        var existing = await Matching(db.Corrections, hash, encounter).ToListAsync(ct);
        var correction = existing.FirstOrDefault(c => c.Category == category);
        if (value is { } v)
        {
            if (correction is null)
            {
                correction = new CorrectionRecord
                {
                    DemoHash = hash,
                    PlayerSteamId = encounter.PlayerSteamId,
                    OpponentSteamId = encounter.OpponentSteamId,
                    RoundNumber = encounter.RoundNumber,
                    FirstShotTick = encounter.FirstShotTick,
                    Category = category,
                };
                db.Corrections.Add(correction);
                existing.Add(correction);
            }

            correction.Value = v;
            correction.RoundNumber = encounter.RoundNumber;
            correction.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else if (correction is not null)
        {
            db.Corrections.Remove(correction);
            existing.Remove(correction);
        }

        encounter.ApplyCorrections(existing.ToDictionary(c => c.Category, c => c.Value));
        await db.SaveChangesAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
        return (encounter.Flags, encounter.PrimaryCategory);
    }

    /// <summary>Number of category verdicts, optionally only those set after <paramref name="since"/>.</summary>
    public async Task<int> CountAsync(DateTimeOffset? since = null, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var all = await db.Corrections.AsNoTracking().Select(c => c.UpdatedAt).ToListAsync(ct);
        // SQLite can't compare DateTimeOffset in SQL; there are few corrections, so filter here.
        return since is { } s ? all.Count(t => t > s) : all.Count;
    }

    /// <summary>Every stored encounter, of every player, with the user's verdicts where there are any.</summary>
    public async Task<IReadOnlyList<TrainingRow>> TrainingSetAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var corrections = await db.Corrections.AsNoTracking().ToListAsync(ct);
        var byDemo = corrections.GroupBy(c => c.DemoHash).ToDictionary(g => g.Key, g => new CorrectionLookup(g));

        var hashes = await db.Demos.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.ContentHash, ct);
        var records = await db.Encounters.AsNoTracking().OrderBy(e => e.Id).ForFeatures().ToListAsync(ct);
        var rows = records.Select(r => (Hash: hashes[r.DemoId], Record: r));

        return rows
            .Select(r => new TrainingRow(r.Record.Id, r.Record.ToFeatures(), r.Hash, byDemo.GetValueOrDefault(r.Hash)?.For(r.Record)))
            .ToList();
    }

    /// <summary>
    /// Classifies stored encounters again with <paramref name="classifier"/> and re-applies the corrections.
    /// By default only encounters classified by another source are touched, so this is cheap when nothing changed.
    /// </summary>
    /// <returns>How many encounters were reclassified.</returns>
    public async Task<int> ReclassifyAsync(IAimClassifier classifier, bool all = false, CancellationToken ct = default)
    {
        var source = classifier.Source;
        Dictionary<string, CorrectionLookup> byDemo;
        Dictionary<int, string> hashes;
        await using (var db = dbFactory.CreateDbContext())
        {
            byDemo = (await db.Corrections.AsNoTracking().ToListAsync(ct))
                .GroupBy(c => c.DemoHash)
                .ToDictionary(g => g.Key, g => new CorrectionLookup(g));
            hashes = await db.Demos.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.ContentHash, ct);
        }

        var total = 0;
        var lastId = 0;
        while (true)
        {
            await using var db = dbFactory.CreateDbContext();
            var batch = await db.Encounters
                .Where(e => e.Id > lastId && (all || e.ClassifierSource != source))
                .OrderBy(e => e.Id)
                .Take(Batch)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;

            var classifications = classifier.Classify(batch.Select(e => e.ToFeatures()).ToList());
            for (var i = 0; i < batch.Count; i++)
            {
                var corrections = hashes.TryGetValue(batch[i].DemoId, out var hash) ? byDemo.GetValueOrDefault(hash)?.For(batch[i]) : null;
                batch[i].ApplyClassification(classifications[i], source, corrections);
            }

            await db.SaveChangesAsync(ct);
            total += batch.Count;
            lastId = batch[^1].Id;
        }

        if (total > 0)
            Changed?.Invoke(this, EventArgs.Empty);
        return total;
    }

    private static IQueryable<CorrectionRecord> Matching(IQueryable<CorrectionRecord> corrections, string hash, EncounterRecord e) =>
        corrections.Where(c => c.DemoHash == hash && c.PlayerSteamId == e.PlayerSteamId && c.OpponentSteamId == e.OpponentSteamId
            && c.FirstShotTick == e.FirstShotTick);
}
