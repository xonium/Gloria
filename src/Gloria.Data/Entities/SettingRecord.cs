namespace Gloria.Data.Entities;

/// <summary>One app setting, stored with the data so a scratch database (GLORIA_DATA_DIR) has its own.</summary>
public sealed class SettingRecord
{
    public required string Key { get; set; }

    public string Value { get; set; } = "";
}
