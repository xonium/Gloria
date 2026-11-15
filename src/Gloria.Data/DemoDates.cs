using Gloria.Core.Ingestion;
using Gloria.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data;

/// <summary>Lets the user say when a demo was played, since the demo itself doesn't record it.</summary>
public sealed class DemoDates(IDbContextFactory<GloriaDbContext> dbFactory)
{
    /// <summary>
    /// Sets the demo's played-at date, or with null goes back to the automatic one (the .dem.info match time or
    /// the file's timestamps, if the file is still there). Returns the date and source now stored.
    /// </summary>
    public async Task<(DateTimeOffset? PlayedAt, PlayedAtSource Source)?> SetAsync(int demoId, DateTimeOffset? playedAt, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var demo = await db.Demos.FirstOrDefaultAsync(d => d.Id == demoId, ct);
        if (demo is null)
            return null;

        var existing = await db.PlayedAtOverrides.FindAsync([demo.ContentHash], ct);
        if (playedAt is { } at)
        {
            if (existing is null)
                db.PlayedAtOverrides.Add(new PlayedAtOverrideRecord { ContentHash = demo.ContentHash, PlayedAt = at.ToUniversalTime() });
            else
                existing.PlayedAt = at.ToUniversalTime();

            demo.PlayedAt = at.ToUniversalTime();
            demo.PlayedAtSource = PlayedAtSource.User;
        }
        else
        {
            if (existing is not null)
                db.PlayedAtOverrides.Remove(existing);

            if (demo.FilePath is { } path && File.Exists(path))
                (demo.PlayedAt, demo.PlayedAtSource) = DemoDate.Find(path);
            else if (demo.PlayedAtSource == PlayedAtSource.User)
                // The file is gone, so the automatic date can't be worked out again; keep the date but stop calling it the user's.
                demo.PlayedAtSource = PlayedAtSource.FileTime;
        }

        await db.SaveChangesAsync(ct);
        return (demo.PlayedAt, demo.PlayedAtSource);
    }
}
