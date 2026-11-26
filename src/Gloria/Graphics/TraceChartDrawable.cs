using Gloria.Services;

namespace Gloria.Graphics;

/// <summary>
/// A line over an encounter's time axis: seconds from first sighting, with the lead-in before it dashed.
/// Shots are marked on the line, filled for a hit, and a playhead follows the replay.
/// </summary>
public sealed class TraceChartDrawable : IDrawable
{
	private const float Left = 48;
	private const float Right = 16;
	private const float Top = 14;
	private const float Bottom = 24;

	/// <summary>Seconds of each sample relative to first sighting.</summary>
	public float[] Seconds { get; set; } = [];

	public float[] Values { get; set; } = [];

	public float MinY { get; set; }

	public float MaxY { get; set; } = 1;

	/// <summary>A shaded band, e.g. "on the body".</summary>
	public (float Low, float High)? Band { get; set; }

	/// <summary>A dashed reference line, e.g. the weapon's accurate speed.</summary>
	public float? Threshold { get; set; }

	public string? ThresholdLabel { get; set; }

	public Func<float, string> FormatY { get; set; } = v => $"{v:0}";

	public IReadOnlyList<(int Index, bool Hit)> Shots { get; set; } = [];

	/// <summary>Sample index of a labeled moment, e.g. where the flick started, or negative for none.</summary>
	public int Mark { get; set; } = -1;

	public string? MarkLabel { get; set; }

	/// <summary>Sample index of the playhead, or negative for none.</summary>
	public float Playhead { get; set; } = -1;

	public void Draw(ICanvas canvas, RectF rect)
	{
		if (Values.Length < 2)
			return;

		var plot = new RectF(rect.Left + Left, rect.Top + Top, rect.Width - Left - Right, rect.Height - Top - Bottom);
		var t0 = Seconds[0];
		var t1 = Seconds[^1];
		var span = Math.Max(t1 - t0, 0.01f);
		float X(float t) => plot.Left + (t - t0) / span * plot.Width;
		float Y(float v) => plot.Bottom - (Math.Clamp(v, MinY, MaxY) - MinY) / (MaxY - MinY) * plot.Height;

		if (Band is { } band)
		{
			canvas.FillColor = Color.FromArgb("#1d3354");
			canvas.FillRectangle(plot.Left, Y(band.High), plot.Width, Y(band.Low) - Y(band.High));
		}

		canvas.FontColor = Display.Muted;
		canvas.FontSize = 11;
		canvas.StrokeSize = 1;
		foreach (var v in ChartTicks.Nice(MinY, MaxY, 4))
		{
			canvas.StrokeColor = Math.Abs(v) < 1e-6 ? Display.LineStrong : Display.Line;
			canvas.DrawLine(plot.Left, Y(v), plot.Right, Y(v));
			canvas.DrawString(FormatY(v), rect.Left, Y(v) - 7, Left - 8, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
		}

		foreach (var t in ChartTicks.Nice(t0, t1, 6))
			canvas.DrawString(Math.Abs(t) < 1e-6 ? "0 s" : $"{t:0.##}", X(t) - 30, plot.Bottom + 6, 60, 14, HorizontalAlignment.Center, VerticalAlignment.Top);

		if (Threshold is { } threshold)
		{
			canvas.StrokeColor = Display.Text2;
			canvas.StrokeDashPattern = [6, 5];
			canvas.DrawLine(plot.Left, Y(threshold), plot.Right, Y(threshold));
			canvas.StrokeDashPattern = null;
			if (ThresholdLabel is not null)
			{
				canvas.FontColor = Display.Text2;
				canvas.DrawString(ThresholdLabel, plot.Left, Y(threshold) - 18, plot.Width - 4, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
			}
		}

		// First sighting.
		if (t0 < 0)
		{
			canvas.StrokeColor = Display.Muted;
			canvas.StrokeDashPattern = [4, 4];
			canvas.DrawLine(X(0), plot.Top, X(0), plot.Bottom);
			canvas.StrokeDashPattern = null;
		}

		if (Mark >= 0 && Mark < Values.Length)
		{
			var x = X(Seconds[Mark]);
			canvas.StrokeColor = Display.Warn;
			canvas.StrokeDashPattern = [2, 3];
			canvas.DrawLine(x, plot.Top, x, plot.Bottom);
			canvas.StrokeDashPattern = null;
			if (MarkLabel is not null)
			{
				canvas.FontColor = Display.Warn;
				canvas.DrawString(MarkLabel, x + 4, plot.Top, 120, 14, HorizontalAlignment.Left, VerticalAlignment.Top);
			}
		}

		canvas.StrokeColor = Display.Accent;
		canvas.StrokeSize = 2;
		canvas.StrokeLineJoin = LineJoin.Round;
		var lead = new PathF(X(Seconds[0]), Y(Values[0]));
		var main = new PathF();
		var mainStarted = false;
		for (var i = 1; i < Values.Length; i++)
		{
			var p = new PointF(X(Seconds[i]), Y(Values[i]));
			if (Seconds[i] <= 0)
			{
				lead.LineTo(p);
				continue;
			}

			if (!mainStarted)
			{
				main.MoveTo(X(Seconds[i - 1]), Y(Values[i - 1]));
				mainStarted = true;
			}

			main.LineTo(p);
		}

		canvas.StrokeDashPattern = [3, 4];
		canvas.DrawPath(lead);
		canvas.StrokeDashPattern = null;
		canvas.DrawPath(main);

		foreach (var (index, hit) in Shots)
		{
			if (index < 0 || index >= Values.Length)
				continue;

			var p = new PointF(X(Seconds[index]), Y(Values[index]));
			canvas.StrokeColor = Display.Line;
			canvas.StrokeSize = 1;
			canvas.DrawLine(p.X, plot.Top, p.X, plot.Bottom);
			canvas.StrokeSize = 2;
			canvas.StrokeColor = Display.Accent;
			canvas.FillColor = hit ? Display.Accent : Display.Panel;
			canvas.FillCircle(p, 5);
			canvas.DrawCircle(p, 5);
		}

		if (Playhead >= 0)
		{
			var i = Math.Clamp((int)Playhead, 0, Values.Length - 1);
			canvas.StrokeColor = Color.FromArgb("#e3eefc");
			canvas.StrokeSize = 1.5f;
			canvas.DrawLine(X(Seconds[i]), plot.Top, X(Seconds[i]), plot.Bottom);
		}
	}
}
