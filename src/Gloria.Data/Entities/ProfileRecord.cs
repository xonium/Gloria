namespace Gloria.Data.Entities;

/// <summary>A tracked player. Each profile is one SteamID and only ever sees that player's results.</summary>
public sealed class ProfileRecord
{
    public int Id { get; set; }

    public ulong SteamId { get; set; }

    public string Name { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
