using Gloria.Core.Analysis;
using Gloria.Services;

namespace Gloria.Graphics;

/// <summary>
/// The encounter seen through the player's eyes, relative to the opponent: a body drawn to scale at the
/// encounter's distance stays still and the crosshair path moves over it, in degrees of view angle.
/// Each shot shows a ring where the crosshair was on the tick before it and a diamond where the bullet went
/// (crosshair plus recoil), joined by a dashed line. Traces stored without bullet data draw the diamond at the crosshair.
/// </summary>
public sealed class ReplayDrawable : IDrawable
{
	// Body parts in game units relative to the upper chest (eye height minus 12).
	private const float HeadCenter = 10;
	private const float HeadRadius = 5;
	private const float Shoulders = 6;
	private const float Hips = -32;
	private const float Feet = -52;
	private const float TorsoHalfWidth = 10;

	private static readonly Color CrosshairColor = Color.FromArgb("#e3eefc");

	public EncounterTrace? Trace { get; set; }

	/// <summary>Sample index of the encounter start (first sighting).</summary>
	public int StartIndex { get; set; }

	public float Position { get; set; }

	public string OpponentLabel { get; set; } = "";

	public void Draw(ICanvas canvas, RectF rect)
	{
		canvas.FillColor = Color.FromArgb("#111316");
		canvas.FillRoundedRectangle(rect, 8);
		if (Trace is not { Count: > 0 } trace)
			return;

		var current = Math.Clamp((int)Position, 0, trace.Count - 1);
		var distance = Math.Max(trace.Distance[Math.Clamp(StartIndex, 0, trace.Count - 1)], 50f);
		float Deg(float units) => float.RadiansToDegrees(MathF.Atan(units / distance));

		// Fit the whole path from first sighting, plus the body, keeping degrees square.
		var minX = Deg(-TorsoHalfWidth * 2);
		var maxX = Deg(TorsoHalfWidth * 2);
		var minY = Deg(Feet);
		var maxY = Deg(HeadCenter + HeadRadius);
		for (var i = Math.Max(StartIndex - 8, 0); i < trace.Count; i++)
		{
			minX = Math.Min(minX, trace.ErrorX[i]);
			maxX = Math.Max(maxX, trace.ErrorX[i]);
			minY = Math.Min(minY, trace.ErrorY[i]);
			maxY = Math.Max(maxY, trace.ErrorY[i]);
		}

		if (trace is { BulletX: { } bulletX, BulletY: { } bulletY })
		{
			foreach (var shot in trace.Shots)
			{
				var i = trace.IndexOf(shot.Tick - 1);
				minX = Math.Min(minX, bulletX[i]);
				maxX = Math.Max(maxX, bulletX[i]);
				minY = Math.Min(minY, bulletY[i]);
				maxY = Math.Max(maxY, bulletY[i]);
			}
		}

		var spanX = Math.Max(maxX - minX, 4f) * 1.25f;
		var spanY = Math.Max(maxY - minY, 3f) * 1.25f;
		var scale = Math.Min(rect.Width / spanX, rect.Height / spanY);
		var cx = (minX + maxX) / 2;
		var cy = (minY + maxY) / 2;
		PointF P(float x, float y) => new(rect.Center.X + (x - cx) * scale, rect.Center.Y - (y - cy) * scale);

		DrawGrid(canvas, rect, scale, cx, cy, P);

		// The body, to scale.
		canvas.FillColor = Color.FromArgb("#2b2e34");
		canvas.StrokeColor = Color.FromArgb("#6b7079");
		canvas.StrokeSize = 1.5f;
		var topLeft = P(Deg(-TorsoHalfWidth), Deg(Shoulders));
		var hipRight = P(Deg(TorsoHalfWidth), Deg(Hips));
		var torso = new RectF(topLeft.X, topLeft.Y, hipRight.X - topLeft.X, hipRight.Y - topLeft.Y);
		canvas.FillRoundedRectangle(torso, 4);
		canvas.DrawRoundedRectangle(torso, 4);
		foreach (var side in new[] { -1f, 1f })
		{
			var legTop = P(Deg(side < 0 ? -TorsoHalfWidth * 0.9f : TorsoHalfWidth * 0.1f), Deg(Hips));
			var legBottom = P(Deg(side < 0 ? -TorsoHalfWidth * 0.1f : TorsoHalfWidth * 0.9f), Deg(Feet));
			var leg = new RectF(legTop.X, legTop.Y, legBottom.X - legTop.X, legBottom.Y - legTop.Y);
			canvas.FillRoundedRectangle(leg, 3);
			canvas.DrawRoundedRectangle(leg, 3);
		}

		var head = P(0, Deg(HeadCenter));
		var headRadius = Deg(HeadRadius) * scale;
		canvas.FillCircle(head, headRadius);
		canvas.DrawCircle(head, headRadius);
		canvas.FontColor = Display.Muted;
		canvas.FontSize = 12;
		var feet = P(0, Deg(Feet));
		canvas.DrawString(OpponentLabel, feet.X - 150, feet.Y + 6, 300, 16, HorizontalAlignment.Center, VerticalAlignment.Top);

		// Crosshair path up to now; the lead-in before first sighting is fainter.
		canvas.StrokeSize = 2;
		canvas.StrokeLineJoin = LineJoin.Round;
		for (var i = 1; i <= current; i++)
		{
			canvas.StrokeColor = i <= StartIndex ? Display.Accent.WithAlpha(0.3f) : Display.Accent.WithAlpha(0.8f);
			canvas.DrawLine(P(trace.ErrorX[i - 1], trace.ErrorY[i - 1]), P(trace.ErrorX[i], trace.ErrorY[i]));
		}

		foreach (var shot in trace.Shots)
		{
			var index = trace.IndexOf(shot.Tick - 1);
			if (index > current)
				continue;

			var p = P(trace.ErrorX[index], trace.ErrorY[index]);
			if (trace is { BulletX: { } bx, BulletY: { } by })
			{
				// Ring where the crosshair aimed, a dashed line to where recoil sent the bullet.
				var aimed = p;
				p = P(bx[index], by[index]);
				canvas.StrokeColor = Display.Bullet.WithAlpha(0.7f);
				canvas.StrokeSize = 1.5f;
				canvas.StrokeDashPattern = [4, 3];
				canvas.DrawLine(aimed, p);
				canvas.StrokeDashPattern = null;
				canvas.StrokeColor = CrosshairColor;
				canvas.DrawCircle(aimed, 6);
			}

			canvas.StrokeSize = 2;
			var diamond = new PathF(p.X, p.Y - 8);
			diamond.LineTo(p.X + 8, p.Y);
			diamond.LineTo(p.X, p.Y + 8);
			diamond.LineTo(p.X - 8, p.Y);
			diamond.Close();
			canvas.FillColor = shot.Hit ? Display.Bullet : Color.FromArgb("#111316");
			canvas.FillPath(diamond);
			canvas.StrokeColor = Display.Bullet;
			canvas.DrawPath(diamond);
		}

		var crosshair = P(trace.ErrorX[current], trace.ErrorY[current]);
		canvas.StrokeColor = CrosshairColor;
		canvas.StrokeSize = 1.5f;
		canvas.DrawCircle(crosshair, 11);
		canvas.DrawLine(crosshair.X - 20, crosshair.Y, crosshair.X - 7, crosshair.Y);
		canvas.DrawLine(crosshair.X + 7, crosshair.Y, crosshair.X + 20, crosshair.Y);
		canvas.DrawLine(crosshair.X, crosshair.Y - 20, crosshair.X, crosshair.Y - 7);
		canvas.DrawLine(crosshair.X, crosshair.Y + 7, crosshair.X, crosshair.Y + 20);
	}

	private static void DrawGrid(ICanvas canvas, RectF rect, float scale, float cx, float cy, Func<float, float, PointF> P)
	{
		var degreesWide = rect.Width / scale;
		var step = degreesWide switch { < 6 => 0.5f, < 12 => 1f, < 30 => 2f, < 60 => 5f, _ => 10f };
		var left = cx - rect.Width / 2 / scale;
		var right = cx + rect.Width / 2 / scale;
		var bottom = cy - rect.Height / 2 / scale;
		var top = cy + rect.Height / 2 / scale;
		canvas.StrokeSize = 1;
		canvas.FontColor = Display.Muted;
		canvas.FontSize = 11;
		for (var x = MathF.Ceiling(left / step) * step; x <= right; x += step)
		{
			canvas.StrokeColor = Math.Abs(x) < 1e-4 ? Display.Line : Color.FromArgb("#1f2227");
			canvas.DrawLine(P(x, top), P(x, bottom));
			var label = P(x, bottom);
			canvas.DrawString($"{x:0.#}°", label.X - 30, label.Y - 18, 60, 14, HorizontalAlignment.Center, VerticalAlignment.Center);
		}

		for (var y = MathF.Ceiling(bottom / step) * step; y <= top; y += step)
		{
			canvas.StrokeColor = Math.Abs(y) < 1e-4 ? Display.Line : Color.FromArgb("#1f2227");
			canvas.DrawLine(P(left, y), P(right, y));
		}
	}
}
