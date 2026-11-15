using Gloria.Core.Maps;
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Gloria.Services;

/// <summary>A map's radar images and how they line up with the world.</summary>
/// <param name="Lower">The lower level's radar, for maps that have one (Nuke, Vertigo, Train).</param>
public sealed record MapArt(MapProjection Projection, IImage Upper, IImage? Lower);

/// <summary>
/// Pre-rendered frames of a team's agent model (tools/Gloria.Sprites), one per pose and yaw step. Each frame spans
/// [<see cref="Bottom"/>, <see cref="Top"/>] game units around the feet vertically and ±<see cref="HalfWidth"/> across.
/// </summary>
public sealed class AgentSprites(int step, float bottom, float top, float halfWidth, IReadOnlyDictionary<(bool Crouch, int Yaw), IImage> frames)
{
	public float Bottom { get; } = bottom;

	public float Top { get; } = top;

	public float HalfWidth { get; } = halfWidth;

	/// <summary>
	/// The frame for an agent turned <paramref name="yaw"/> degrees counter-clockwise (seen from above) from facing the viewer.
	/// </summary>
	public IImage? Frame(bool crouch, float yaw)
	{
		var snapped = (int)MathF.Round(yaw / step) * step;
		snapped = ((snapped % 360) + 360) % 360;
		return frames.GetValueOrDefault((crouch, snapped));
	}
}

/// <summary>
/// Loads the bundled game art (Resources/Raw, from build/fetch-assets.ps1) once and keeps it: radars here, so
/// drawables never touch files while they draw.
/// </summary>
public sealed class GameArt
{
	private readonly Dictionary<string, MapArt?> _maps = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, AgentSprites?> _agents = new(StringComparer.OrdinalIgnoreCase);
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

	/// <summary>The agent frames of a team ("ct" or "t"), or null when none are bundled.</summary>
	public async Task<AgentSprites?> AgentAsync(string team)
	{
		if (_agents.TryGetValue(team, out var cached))
			return cached;

		AgentSprites? sprites = null;
		try
		{
			using var meta = System.Text.Json.JsonDocument.Parse(await ReadTextAsync("agents/agents.json"));
			var root = meta.RootElement;
			var step = root.GetProperty("step").GetInt32();
			var frames = new Dictionary<(bool, int), IImage>();
			foreach (var crouch in new[] { false, true })
			{
				for (var yaw = 0; yaw < 360; yaw += step)
				{
					if (await LoadImageAsync($"agents/{team}_{(crouch ? "crouch" : "stand")}_{yaw:000}.png") is { } image)
						frames[(crouch, yaw)] = image;
				}
			}

			if (frames.Count > 0)
			{
				sprites = new AgentSprites(
					step,
					root.GetProperty("bottom").GetSingle(),
					root.GetProperty("top").GetSingle(),
					root.GetProperty("halfWidth").GetSingle(),
					frames);
			}
		}
		catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or KeyNotFoundException)
		{
			System.Diagnostics.Debug.WriteLine($"No agent sprites for {team}: {ex.Message}");
		}

		_agents[team] = sprites;
		return sprites;
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
