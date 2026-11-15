using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gloria.Data;

/// <summary>Lets <c>dotnet ef</c> create the context without using the MAUI app as startup project.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GloriaDbContext>
{
    public GloriaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GloriaDbContext>()
            .UseSqlite(DatabaseLocation.ConnectionString("gloria-design.db"))
            .Options;
        return new GloriaDbContext(options);
    }
}
