using Gloria.Core.Maps;

namespace Gloria.Core.Tests.Maps;

public class MapProjectionTests
{
    private const string Catalog = """
        {
          "de_mirage": { "posX": -3230, "posY": 1713, "scale": 5 },
          "de_nuke": { "posX": -3453, "posY": 2887, "scale": 7, "lowerBelow": -495 }
        }
        """;

    [Fact]
    public void ToImage_PutsThePositionCornerAtTheOrigin_AndGrowsDownAsWorldYFalls()
    {
        var mirage = MapProjection.ParseCatalog(Catalog)["de_mirage"];

        Assert.Equal((0f, 0f), mirage.ToImage(-3230, 1713));
        Assert.Equal((100f, 40f), mirage.ToImage(-3230 + 500, 1713 - 200));
    }

    [Fact]
    public void ParseCatalog_IsCaseInsensitive_AndReadsTheLowerLevel()
    {
        var maps = MapProjection.ParseCatalog(Catalog);

        Assert.Null(maps["DE_MIRAGE"].LowerBelow);
        Assert.False(maps["de_mirage"].IsLower(-10000));
        Assert.True(maps["de_nuke"].IsLower(-600));
        Assert.False(maps["de_nuke"].IsLower(-400));
    }

    [Fact]
    public void BundledCatalog_HasThePremierMaps()
    {
        var path = Path.Combine(RepoRoot(), "src", "Gloria", "Resources", "Raw", "maps", "maps.json");
        var maps = MapProjection.ParseCatalog(File.ReadAllText(path));

        foreach (var name in new[] { "de_ancient", "de_anubis", "de_dust2", "de_inferno", "de_mirage", "de_nuke", "de_overpass", "de_train" })
        {
            Assert.True(maps.ContainsKey(name), name);
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, $"{name}.png")), name);
        }

        Assert.Equal(-495, maps["de_nuke"].LowerBelow);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "de_nuke_lower.png")));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gloria.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
