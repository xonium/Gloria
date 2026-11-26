using Gloria.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data;

public sealed class GloriaDbContext(DbContextOptions<GloriaDbContext> options) : DbContext(options)
{
    public DbSet<DemoRecord> Demos => Set<DemoRecord>();

    public DbSet<DemoPlayerRecord> DemoPlayers => Set<DemoPlayerRecord>();

    public DbSet<RoundRecord> Rounds => Set<RoundRecord>();

    public DbSet<SprayBulletRecord> SprayBullets => Set<SprayBulletRecord>();

    public DbSet<EncounterRecord> Encounters => Set<EncounterRecord>();

    public DbSet<ProfileRecord> Profiles => Set<ProfileRecord>();

    public DbSet<SettingRecord> Settings => Set<SettingRecord>();

    public DbSet<CorrectionRecord> Corrections => Set<CorrectionRecord>();

    public DbSet<PlayedAtOverrideRecord> PlayedAtOverrides => Set<PlayedAtOverrideRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DemoRecord>(demo =>
        {
            demo.HasIndex(d => d.ContentHash).IsUnique();
            demo.Property(d => d.ContentHash).HasMaxLength(64);
            demo.HasMany(d => d.Players).WithOne(p => p.Demo).HasForeignKey(p => p.DemoId);
            demo.HasMany(d => d.Rounds).WithOne(r => r.Demo).HasForeignKey(r => r.DemoId);
            demo.HasMany(d => d.Encounters).WithOne(e => e.Demo).HasForeignKey(e => e.DemoId);
            demo.HasMany(d => d.SprayBullets).WithOne(s => s.Demo).HasForeignKey(s => s.DemoId);
        });

        modelBuilder.Entity<SprayBulletRecord>(spray =>
        {
            spray.HasKey(s => new { s.DemoId, s.SteamId, s.Weapon, s.Bullet });
            spray.HasIndex(s => new { s.SteamId, s.DemoId });
            spray.Property(s => s.Weapon).HasMaxLength(32);
        });

        modelBuilder.Entity<DemoPlayerRecord>(player =>
        {
            player.HasKey(p => new { p.DemoId, p.SteamId });
            player.HasIndex(p => p.SteamId);
        });

        modelBuilder.Entity<RoundRecord>(round => round.HasKey(r => new { r.DemoId, r.Number }));

        modelBuilder.Entity<EncounterRecord>(encounter => encounter.HasIndex(e => new { e.PlayerSteamId, e.DemoId }));

        modelBuilder.Entity<ProfileRecord>(profile => profile.HasIndex(p => p.SteamId).IsUnique());

        modelBuilder.Entity<SettingRecord>(setting => setting.HasKey(s => s.Key));

        modelBuilder.Entity<PlayedAtOverrideRecord>(date =>
        {
            date.HasKey(d => d.ContentHash);
            date.Property(d => d.ContentHash).HasMaxLength(64);
        });

        modelBuilder.Entity<CorrectionRecord>(correction =>
        {
            correction.Property(c => c.DemoHash).HasMaxLength(64);
            correction.HasIndex(c => new { c.DemoHash, c.PlayerSteamId, c.OpponentSteamId, c.FirstShotTick, c.Category }).IsUnique();
        });
    }
}
