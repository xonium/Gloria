using System.Text.Json;
using Gloria.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gloria.Data;

/// <summary>
/// App settings kept in the database rather than in MAUI's per-app preferences, so they follow
/// <see cref="DatabaseLocation.DataDirectory"/>. Reads and writes are small and synchronous.
/// </summary>
public sealed class AppSettings(IDbContextFactory<GloriaDbContext> dbFactory)
{
    public const string DemoFoldersKey = "demo-folders";
    public const string CurrentProfileKey = "current-profile";

    /// <summary>Folders rescanned for new demos at startup.</summary>
    public IReadOnlyList<string> DemoFolders =>
        Get(DemoFoldersKey) is { Length: > 0 } json ? JsonSerializer.Deserialize<List<string>>(json) ?? [] : [];

    /// <summary>SteamID of the profile being viewed, or null if none was chosen.</summary>
    public ulong? CurrentProfile
    {
        get => ulong.TryParse(Get(CurrentProfileKey), out var steamId) ? steamId : null;
        set => Set(CurrentProfileKey, value?.ToString());
    }

    public void AddDemoFolder(string folder) =>
        SetDemoFolders(DemoFolders.Append(folder).Distinct(StringComparer.OrdinalIgnoreCase));

    public void RemoveDemoFolder(string folder) =>
        SetDemoFolders(DemoFolders.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)));

    public string? Get(string key)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefault();
    }

    /// <summary>Stores the value, or removes the setting when it is null.</summary>
    public void Set(string key, string? value)
    {
        using var db = dbFactory.CreateDbContext();
        var setting = db.Settings.Find(key);
        if (value is null)
        {
            if (setting is not null)
                db.Settings.Remove(setting);
        }
        else if (setting is null)
        {
            db.Settings.Add(new SettingRecord { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        db.SaveChanges();
    }

    private void SetDemoFolders(IEnumerable<string> folders) => Set(DemoFoldersKey, JsonSerializer.Serialize(folders.ToList()));
}
