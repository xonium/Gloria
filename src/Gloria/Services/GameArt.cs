using Gloria.Core.Maps;
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Gloria.Services;

/// <summary>A map's radar images and how they line up with the world.</summary>
/// <param name="Lower">The lower level's radar, for maps that have one (Nuke, Vertigo, Train).</param>
public sealed record MapArt(MapProjection Projection, IImage Upper, IImage? Lower);

/// <summary>
/// Loads the bundled game art (Resources/Raw, from build/fetch-assets.ps1) once and keeps it: radars here, so
/// drawables never touch files while they draw.
/// </summary>
public sealed class GameArt
{
	private readonly Dictionary<string, MapArt?> _maps = new(StringComparer.OrdinalIgnoreCase);
	private IReadOnlyDictionary<string, MapProjection>? _catalog;

	/// <summary>The radar of <paramref name="mapName"/>, or null when none is bundled.</summary>
	public async Task<MapArt?> MapAsync(string? mapName)
	{
		// Workshop maps come as "workshop/<id>/<name>".
		var name = mapName?.Split('/').Last() ?? "";
		if (_maps.TryGetValue(name, out var cached))
			return cached;

		MapArt? art = null;
		try
		{
			_catalog ??= MapProjection.ParseCatalog(await ReadTextAsync("maps/maps.json"));
			if (_catalog.TryGetValue(name, out var projection))
			{
				var upper = await LoadImageAsync($"maps/{projection.Name}.png");
				var lower = projection.LowerBelow is not null ? await LoadImageAsync($"maps/{projection.Name}_lower.png") : null;
				art = upper is null ? null : new MapArt(projection, upper, lower);
			}
		}
		catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
		{
			System.Diagnostics.Debug.WriteLine($"No radar for {name}: {ex.Message}");
		}

		_maps[name] = art;
		return art;
	}

	private static async Task<string> ReadTextAsync(string path)
	{
		await using var stream = await FileSystem.OpenAppPackageFileAsync(path);
		using var reader = new StreamReader(stream);
		return await reader.ReadToEndAsync();
	}

	private static async Task<IImage?> LoadImageAsync(string path)
	{
		try
		{
			await using var stream = await FileSystem.OpenAppPackageFileAsync(path);
			// PlatformImage reads the whole stream; copy it so the package stream can close.
			var buffer = new MemoryStream();
			await stream.CopyToAsync(buffer);
			buffer.Position = 0;
			return PlatformImage.FromStream(buffer);
		}
		catch (FileNotFoundException)
		{
			return null;
		}
	}
}
