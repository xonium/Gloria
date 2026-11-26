using Gloria.Services;

namespace Gloria.Graphics;

/// <summary>One player's lines on the spray chart.</summary>
/// <param name="Reach">Per bullet from 1, the share of sprays that got there (0–1).</param>
/// <param name="HitRate">Per bullet from 1, the share of those bullets that hit (0–1); NaN where too few were fired.</param>
/// <param name="KillBullet">The bullet that killed most often, or null.</param>
public sealed record SprayLine(IReadOnlyList<double> Reach, IReadOnlyList<double> HitRate, int? KillBullet, Color Color);

/// <summary>
/// Sprays with one gun over its magazine, bullet 1 at the left: per player, how many sprays reached each bullet
/// (solid) and how often that bullet hit (dashed), with a marker over the bullet that killed most often.
/// The first player is drawn on top.
/// </summary>
public sealed class SprayChartDrawable : IDrawable
{
	private const float Left = 44;
	private const float Right = 16;
	private const float Top = 26;
	private const float Bottom = 24;
	private const float MarkerSize = 6;

	/// <summary>The magazine size: the last bullet on the x axis.</summary>
	public int Clip { get; init; } = 30;

	public IReadOnlyList<SprayLine> Lines { get; init; } = [];

	public void Draw(ICanvas canvas, RectF rect)
	{
		var plot = new RectF(rect.Left + Left, rect.Top + Top, rect.Width - Left - Right, rect.Height - Top - Bottom);
		var span = Math.Max(Clip - 1, 1);
		float X(int bullet) => plot.Left + (bullet - 1) / (float)span * plot.Width;
		float Y(double share) => (float)(plot.Bottom - Math.Clamp(share, 0, 1) * plot.Height);

		canvas.FontColor = Display.Muted;
		canvas.FontSize = 11;
		canvas.StrokeSize = 1;
		foreach (var share in new[] { 0, 0.25, 0.5, 0.75, 1 })
		{
			canvas.StrokeColor = share == 0 ? Display.LineStrong : Display.Line;
			canvas.DrawLine(plot.Left, Y(share), plot.Right, Y(share));
			canvas.DrawString($"{share * 100:0}%", rect.Left, Y(share) - 7, Left - 8, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
		}

		// Bullet 1, then round numbers.
		var ticks = ChartTicks.Nice(0, Clip, Math.Clamp((int)(plot.Width / 60), 2, 15)).Select(t => (int)t).Where(t => t > 1).Prepend(1).Distinct();
		foreach (var bullet in ticks)
		{
			canvas.StrokeColor = Display.Line;
			canvas.DrawLine(X(bullet), plot.Bottom, X(bullet), plot.Bottom + 4);
			canvas.DrawString($"{bullet}", X(bullet) - 20, plot.Bottom + 6, 40, 14, HorizontalAlignment.Center, VerticalAlignment.Top);
		}

		// Kill markers stack upwards when players share a bullet.
		var stacked = new Dictionary<int, int>();
		for (var i = Lines.Count - 1; i >= 0; i--)
		{
			var line = Lines[i];
			if (Lines.Count == 1)
				FillReach(canvas, line, plot, X, Y);

			if (line.KillBullet is { } kill && kill >= 1 && kill <= Clip)
			{
				var x = X(kill);
				canvas.StrokeColor = line.Color.WithAlpha(0.45f);
				canvas.StrokeSize = 1;
				canvas.StrokeDashPattern = [2, 3];
				canvas.DrawLine(x, plot.Top, x, plot.Bottom);
				canvas.StrokeDashPattern = null;

				var level = stacked.GetValueOrDefault(kill);
				stacked[kill] = level + 1;
				var tip = plot.Top - 4 - level * (MarkerSize + 2);
				var marker = new PathF(x, tip);
				marker.LineTo(x - MarkerSize, tip - MarkerSize);
				marker.LineTo(x + MarkerSize, tip - MarkerSize);
				marker.Close();
				canvas.FillColor = line.Color;
				canvas.FillPath(marker);
			}
		}

		canvas.StrokeLineJoin = LineJoin.Round;
		canvas.StrokeSize = 2;
		for (var i = Lines.Count - 1; i >= 0; i--)
		{
			var line = Lines[i];
			canvas.StrokeColor = line.Color;
			canvas.FillColor = line.Color;
			DrawLine(canvas, line.Reach, X, Y);
			canvas.StrokeDashPattern = [5, 4];
			DrawLine(canvas, line.HitRate, X, Y);
			canvas.StrokeDashPattern = null;
		}
	}

	private void FillReach(ICanvas canvas, SprayLine line, RectF plot, Func<int, float> x, Func<double, float> y)
	{
		var area = new PathF(x(1), plot.Bottom);
		for (var b = 1; b <= Math.Min(Clip, line.Reach.Count); b++)
			area.LineTo(x(b), y(double.IsNaN(line.Reach[b - 1]) ? 0 : line.Reach[b - 1]));
		area.LineTo(x(Math.Min(Clip, line.Reach.Count)), plot.Bottom);
		area.Close();
		canvas.FillColor = line.Color.WithAlpha(0.12f);
		canvas.FillPath(area);
	}

	/// <summary>Draws the values as a line from bullet 1, broken where they are NaN; a lone value gets a dot in the fill color.</summary>
	private void DrawLine(ICanvas canvas, IReadOnlyList<double> values, Func<int, float> x, Func<double, float> y)
	{
		PathF? path = null;
		var points = 0;
		void Flush()
		{
			if (path is not null && points > 1)
				canvas.DrawPath(path);
			else if (path is not null)
				canvas.FillCircle(path.FirstPoint, 2.5f);

			path = null;
			points = 0;
		}

		for (var b = 1; b <= Math.Min(Clip, values.Count); b++)
		{
			var v = values[b - 1];
			if (double.IsNaN(v))
			{
				Flush();
				continue;
			}

			var p = new PointF(x(b), y(v));
			if (path is null)
				path = new PathF(p);
			else
				path.LineTo(p);
			points++;
		}

		Flush();
	}}
