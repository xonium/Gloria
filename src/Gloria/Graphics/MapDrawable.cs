using Gloria.Core.Analysis;
using Gloria.Core.Maps;
using Gloria.Services;

namespace Gloria.Graphics;

/// <summary>
/// Where the encounter happened, seen from above on the map's radar: both players' paths up to now, which way
/// each is facing, the line between them, where the player stood at each shot and where the kill was. The view
/// zooms to the area around both paths; the radar level follows the player's altitude on two-level maps.
/// </summary>
public sealed class MapDrawable : IDrawable
{
	// The view shows at least this much of the world across, so a close duel still shows its surroundings.
	private const float MinSpanUnits = 1400;

	private static readonly Color Background = Color.FromArgb("#111316");

	public EncounterTrace? Trace { get; set; }

	public MapArt? Art { get; set; }

	public string MapName { get; set; } = "";

	public string OpponentLabel { get; set; } = "";

	/// <summary>Sample index of the encounter start (first sighting).</summary>
	public int StartIndex { get; set; }

	public float Position { get; set; }

	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Background;
		canvas.FillRoundedRectangle(rect, 8);
		if (Trace?.Scene is not { } scene || Art is not { } art)
		{
			canvas.FontColor = Display.Muted;
			canvas.FontSize = 13;
			var message = Trace?.Scene is null ? "Re-analyze this demo to see the map" : $"No map for {MapName}";
			canvas.DrawString(message, rect, HorizontalAlignment.Center, VerticalAlignment.Center);
			return;
		}

		var trace = Trace;
		var map = art.Projection;
		var current = Math.Clamp((int)Position, 0, trace.Count - 1);
		var from = Math.Max(StartIndex - 16, 0);

		// Zoom to both paths in radar pixels, square, with a margin.
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		for (var i = from; i < trace.Count; i++)
		{
			foreach (var (wx, wy) in new[] { (scene.PlayerX[i], scene.PlayerY[i]), (scene.OpponentX[i], scene.OpponentY[i]) })
			{
				var (px, py) = map.ToImage(wx, wy);
				minX = Math.Min(minX, px);
				maxX = Math.Max(maxX, px);
				minY = Math.Min(minY, py);
				maxY = Math.Max(maxY, py);
			}
		}

		var span = Math.Max(Math.Max(maxX - minX, maxY - minY) * 1.5f, MinSpanUnits / map.Scale);
		var zoom = Math.Min(rect.Width, rect.Height) / span;
		var cx = (minX + maxX) / 2;
		var cy = (minY + maxY) / 2;
		PointF ToScreen(float wx, float wy)
		{
			var (px, py) = map.ToImage(wx, wy);
			return new PointF(rect.Center.X + (px - cx) * zoom, rect.Center.Y + (py - cy) * zoom);
		}

		canvas.SaveState();
		var clip = new PathF();
		clip.AppendRoundedRectangle(rect, 8);
		canvas.ClipPath(clip);

		var lower = map.IsLower(scene.PlayerZ[current]) && art.Lower is not null;
		var image = lower ? art.Lower! : art.Upper;
		var size = MapProjection.ImageSize * zoom;
		canvas.DrawImage(image, rect.Center.X - cx * zoom, rect.Center.Y - cy * zoom, size, size);
		// Dim the radar so the paths stand out.
		canvas.FillColor = Background.WithAlpha(0.35f);
		canvas.FillRectangle(rect);

		DrawPath(canvas, scene.OpponentX, scene.OpponentY, from, current, Display.Loss, ToScreen);
		DrawPath(canvas, scene.PlayerX, scene.PlayerY, from, current, Display.Accent, ToScreen);

		var me = ToScreen(scene.PlayerX[current], scene.PlayerY[current]);
		var them = ToScreen(scene.OpponentX[current], scene.OpponentY[current]);
		var opponentDead = scene.Death is { ByPlayer: true } kill && trace.StartTick + current >= kill.Tick;
		var playerDead = scene.Death is { ByPlayer: false } death && trace.StartTick + current >= death.Tick;

		// Line of sight between the two.
		if (!opponentDead && !playerDead)
		{
			canvas.StrokeColor = Colors.White.WithAlpha(0.35f);
			canvas.StrokeSize = 1;
			canvas.StrokeDashPattern = [4, 4];
			canvas.DrawLine(me, them);
			canvas.StrokeDashPattern = null;
		}

		// Where the player stood at each shot so far.
		foreach (var shot in trace.Shots)
		{
			var index = trace.IndexOf(shot.Tick - 1);
			if (index > current)
				continue;

			var at = ToScreen(scene.PlayerX[index], scene.PlayerY[index]);
			canvas.StrokeColor = Display.Bullet;
			canvas.StrokeSize = 1.5f;
			canvas.FillColor = shot.Hit ? Display.Bullet : Background;
			canvas.FillCircle(at, 3.5f);
			canvas.DrawCircle(at, 3.5f);
		}

		DrawPlayer(canvas, them, scene.OpponentYaw[current], Display.Loss, opponentDead, OpponentLabel);
		DrawPlayer(canvas, me, scene.PlayerYaw[current], Display.Accent, playerDead, "You");
		canvas.RestoreState();

		canvas.FontColor = Display.Text2;
		canvas.FontSize = 12;
		var levelText = art.Lower is null ? "" : lower ? " · lower level" : " · upper level";
		canvas.DrawString($"{map.Name}{levelText}", rect.X + 10, rect.Y + 8, rect.Width - 20, 16, HorizontalAlignment.Left, VerticalAlignment.Top);
	}

	private static void DrawPath(ICanvas canvas, float[] xs, float[] ys, int from, int to, Color color, Func<float, float, PointF> toScreen)
	{
		if (to <= from)
			return;

		var path = new PathF(toScreen(xs[from], ys[from]));
		for (var i = from + 1; i <= to; i++)
			path.LineTo(toScreen(xs[i], ys[i]));

		canvas.StrokeColor = color.WithAlpha(0.85f);
		canvas.StrokeSize = 2;
		canvas.StrokeLineJoin = LineJoin.Round;
		canvas.DrawPath(path);
	}

	/// <summary>A dot with a view cone; yaw is the game's, counter-clockwise from world +X, and screen y points down.</summary>
	private static void DrawPlayer(ICanvas canvas, PointF at, float yaw, Color color, bool dead, string label)
	{
		if (dead)
		{
			canvas.StrokeColor = color;
			canvas.StrokeSize = 2.5f;
			canvas.DrawLine(at.X - 6, at.Y - 6, at.X + 6, at.Y + 6);
			canvas.DrawLine(at.X - 6, at.Y + 6, at.X + 6, at.Y - 6);
		}
		else
		{
			const float coneLength = 46;
			const float halfCone = 22;
			var cone = new PathF(at);
			for (var a = -halfCone; a <= halfCone; a += 4)
			{
				var radians = float.DegreesToRadians(yaw + a);
				cone.LineTo(at.X + MathF.Cos(radians) * coneLength, at.Y - MathF.Sin(radians) * coneLength);
			}

			cone.Close();
			canvas.FillColor = color.WithAlpha(0.22f);
			canvas.FillPath(cone);

			canvas.FillColor = color;
			canvas.StrokeColor = Colors.White;
			canvas.StrokeSize = 1.5f;
			canvas.FillCircle(at, 6);
			canvas.DrawCircle(at, 6);
		}

		canvas.FontColor = Display.Text;
		canvas.FontSize = 11;
		canvas.DrawString(label, at.X + 9, at.Y + 4, 140, 14, HorizontalAlignment.Left, VerticalAlignment.Top);
	}
}
