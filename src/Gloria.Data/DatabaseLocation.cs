namespace Gloria.Data;

/// <summary>
/// Where the database lives. It must stay outside the Velopack install directory
/// (%LocalAppData%\Gloria\), which is replaced on every update.
/// </summary>
public static class DatabaseLocation
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gloria.Data");

    public static string DefaultPath { get; } = Path.Combine(DataDirectory, "gloria.db");

    /// <summary>Creates the data directory if needed and returns the default database path.</summary>
    public static string EnsureDefaultPath()
    {
        Directory.CreateDirectory(DataDirectory);
        return DefaultPath;
    }

    public static string ConnectionString(string databasePath) => $"Data Source={databasePath}";
}
