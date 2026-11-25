using Gloria.Services;

namespace Gloria.Graphics;

/// <summary>One player's line on a shared sparkline.</summary>
/// <param name="Trend">The line, oldest to newest; NaN where there is no value.</param>
public sealed record SparkSeries(IReadOnlyList<double> Trend, Color Color);

/// <summary>
/// A single-series line with a dot on the latest value, oldest to newest. With <see cref="Trend"/> set, the
/// values are drawn as faint dots and the trend (e.g. a rolling median) as the line. With <see cref="Series"/> set,
/// draws several lines on one scale instead, each in its own color and ending at the right edge.
/// </summary>
public sealed class SparklineDrawable : IDrawable
{
	public IReadOnlyList<double> Values { get; set; } = [];

	/// <summary>Same length as <see cref="Values"/>; NaN where there is no trend value.</summary>
	public IReadOnlyList<double>? Trend { get; set; }

	/// <summary>
	/// Several lines on a shared scale, used instead of <see cref="Values"/>. Each is right-aligned, so every
	/// series' latest value is at the right edge, whatever its length.
	/// </summary>
	public IReadOnlyList<SparkSeries>? Series { get; set; }

	public void Draw(ICanvas canvas, RectF rect)
	{
		if (Series is { } series)
		{
			DrawSeries(canvas, rect, series);
			return;
		}

		var known = Values.Concat(Trend ?? []).Where(v => !double.IsNaN(v)).ToList();
		if (known.Count < 2 || Values.Count < 2)
			return;

		var (min, span) = Scale(known);
		var inset = rect.Inflate(-4, -4);
		PointF At(int i, double v) => new(
			inset.Left + inset.Width * i / (Values.Count - 1),
			(float)(inset.Bottom - (v - min) / span * inset.Height));

		if (Trend is not null)
		{
			canvas.FillColor = Display.LineStrong;
			for (var i = 0; i < Values.Count; i++)
			{
				if (!double.IsNaN(Values[i]))
					canvas.FillCircle(At(i, Values[i]), 2.5f);
			}
		}

		DrawLine(canvas, Trend ?? Values, 0, At, Display.Accent);
	}

	private static void DrawSeries(ICanvas canvas, RectF rect, IReadOnlyList<SparkSeries> series)
	{
		var known = series.SelectMany(s => s.Trend).Where(v => !double.IsNaN(v)).ToList();
		var slots = series.Select(s => s.Trend.Count).DefaultIfEmpty(0).Max();
		if (known.Count == 0 || slots == 0)
			return;

		var (min, span) = Scale(known);
		var inset = rect.Inflate(-4, -4);
		PointF At(int slot, double v) => new(
			slots == 1 ? inset.Right : inset.Left + inset.Width * slot / (slots - 1),
			(float)(inset.Bottom - (v - min) / span * inset.Height));

		// The first series (the viewed profile) is drawn last, so it stays on top.
		for (var i = series.Count - 1; i >= 0; i--)
			DrawLine(canvas, series[i].Trend, slots - series[i].Trend.Count, At, series[i].Color);
	}

	/// <summary>The lowest value and the span, padded when every value is the same.</summary>
	private static (double Min, double Span) Scale(List<double> known)
	{
		var min = known.Min();
		var max = known.Max();
		return (min, max - min < 1e-9 ? 1 : max - min);
	}

	private static void DrawLine(ICanvas canvas, IReadOnlyList<double> line, int offset, Func<int, double, PointF> at, Color color)
	{
		PathF? path = null;
		var last = PointF.Zero;
		var points = 0;
		for (var i = 0; i < line.Count; i++)
		{
			if (double.IsNaN(line[i]))
				continue;

			last = at(offset + i, line[i]);
			points++;
			if (path is null)
				path = new PathF(last);
			else
				path.LineTo(last);
		}

		if (path is null)
			return;

		if (points > 1)
		{
			canvas.StrokeColor = color;
			canvas.StrokeSize = 2;
			canvas.StrokeLineJoin = LineJoin.Round;
			canvas.DrawPath(path);
		}

		canvas.FillColor = color;
		canvas.FillCircle(last, 3.5f);
	}
}
