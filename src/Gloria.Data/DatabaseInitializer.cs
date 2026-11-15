using Microsoft.EntityFrameworkCore;

namespace Gloria.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies pending migrations. Run at startup: installed copies keep their old database
    /// across updates, so the schema has to be brought forward in place.
    /// </summary>
    public static void Migrate(IDbContextFactory<GloriaDbContext> factory)
    {
        using var db = factory.CreateDbContext();
        db.Database.Migrate();
    }
}
