using System.Text.Json;

namespace Gloria.Core.Maps;

/// <summary>
/// How a map's radar image lines up with the world, from the game's <c>resource/overviews/&lt;map&gt;.txt</c>:
/// <paramref name="PosX"/>/<paramref name="PosY"/> is the world position of the image's top-left corner and
/// <paramref name="Scale"/> the world units per pixel of the 1024 px radar.
/// </summary>
/// <param name="LowerBelow">Altitude below which the lower level's radar applies; null for single-level maps.</param>
public sealed record MapProjection(string Name, float PosX, float PosY, float Scale, float? LowerBelow = null)
{
    /// <summary>Radar size in pixels; the overview transform is defined for this size.</summary>
    public const float ImageSize = 1024;

    /// <summary>Radar pixel of a world position: x grows to the right (world +X), y downwards (world −Y).</summary>
    public (float X, float Y) ToImage(float worldX, float worldY) => ((worldX - PosX) / Scale, (PosY - worldY) / Scale);

    /// <summary>Whether a position at altitude <paramref name="z"/> is on the lower level's radar.</summary>
    public bool IsLower(float z) => LowerBelow is { } below && z < below;

    /// <summary>Reads <c>maps.json</c> as written by <c>build/fetch-assets.ps1</c>, keyed by map name.</summary>
    public static IReadOnlyDictionary<string, MapProjection> ParseCatalog(string json)
    {
        var maps = new Dictionary<string, MapProjection>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        foreach (var map in document.RootElement.EnumerateObject())
        {
            var v = map.Value;
            maps[map.Name] = new MapProjection(
                map.Name,
                v.GetProperty("posX").GetSingle(),
                v.GetProperty("posY").GetSingle(),
                v.GetProperty("scale").GetSingle(),
                v.TryGetProperty("lowerBelow", out var lower) ? lower.GetSingle() : null);
        }

        return maps;
    }
}
