using Gloria.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data;

public sealed class GloriaDbContext(DbContextOptions<GloriaDbContext> options) : DbContext(options)
{
    public DbSet<DemoRecord> Demos => Set<DemoRecord>();

    public DbSet<DemoPlayerRecord> DemoPlayers => Set<DemoPlayerRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DemoRecord>(demo =>
        {
            demo.HasIndex(d => d.ContentHash).IsUnique();
            demo.Property(d => d.ContentHash).HasMaxLength(64);
            demo.HasMany(d => d.Players).WithOne(p => p.Demo).HasForeignKey(p => p.DemoId);
        });

        modelBuilder.Entity<DemoPlayerRecord>(player =>
        {
            player.HasKey(p => new { p.DemoId, p.SteamId });
            player.HasIndex(p => p.SteamId);
        });
    }
}
