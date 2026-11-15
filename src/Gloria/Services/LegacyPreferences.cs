using Gloria.Data;

namespace Gloria.Services;

/// <summary>
/// Moves the settings that earlier builds kept in MAUI's <see cref="Preferences"/> into the database, once.
/// Only for the default data folder: a scratch database (GLORIA_DATA_DIR) starts empty.
/// </summary>
public static class LegacyPreferences
{
	public static void MoveInto(AppSettings settings)
	{
		if (Environment.GetEnvironmentVariable("GLORIA_DATA_DIR") is { Length: > 0 })
			return;

		var folders = Preferences.Default.Get(AppSettings.DemoFoldersKey, "");
		foreach (var folder in folders.Split('|', StringSplitOptions.RemoveEmptyEntries))
			settings.AddDemoFolder(folder);

		var profile = Preferences.Default.Get(AppSettings.CurrentProfileKey, "");
		if (settings.CurrentProfile is null && ulong.TryParse(profile, out var steamId))
			settings.CurrentProfile = steamId;

		Preferences.Default.Remove(AppSettings.DemoFoldersKey);
		Preferences.Default.Remove(AppSettings.CurrentProfileKey);
	}
}
