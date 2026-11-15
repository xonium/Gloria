using Gloria.Services;

namespace Gloria.Graphics;

/// <summary>
/// A single-series line with a dot on the latest value, oldest to newest. With <see cref="Trend"/> set, the
/// values are drawn as faint dots and the trend (e.g. a rolling median) as the line.
/// </summary>
public sealed class SparklineDrawable : IDrawable
{
	public IReadOnlyList<double> Values { get; set; } = [];

	/// <summary>Same length as <see cref="Values"/>; NaN where there is no trend value.</summary>
	public IReadOnlyList<double>? Trend { get; set; }

	public void Draw(ICanvas canvas, RectF rect)
	{
		var known = Values.Concat(Trend ?? []).Where(v => !double.IsNaN(v)).ToList();
		if (known.Count < 2 || Values.Count < 2)
			return;

		var min = known.Min();
		var max = known.Max();
		var span = max - min < 1e-9 ? 1 : max - min;
		var inset = rect.Inflate(-4, -4);
		PointF At(int i, double v) => new(
			inset.Left + inset.Width * i / (Values.Count - 1),
			(float)(inset.Bottom - (v - min) / span * inset.Height));

		var line = Trend ?? Values;
		if (Trend is not null)
		{
			canvas.FillColor = Display.LineStrong;
			for (var i = 0; i < Values.Count; i++)
			{
				if (!double.IsNaN(Values[i]))
					canvas.FillCircle(At(i, Values[i]), 2.5f);
			}
		}

		PathF? path = null;
		var last = PointF.Zero;
		for (var i = 0; i < line.Count; i++)
		{
			if (double.IsNaN(line[i]))
				continue;

			last = At(i, line[i]);
			if (path is null)
				path = new PathF(last);
			else
				path.LineTo(last);
		}

		if (path is null)
			return;

		canvas.StrokeColor = Display.Accent;
		canvas.StrokeSize = 2;
		canvas.StrokeLineJoin = LineJoin.Round;
		canvas.DrawPath(path);
		canvas.FillColor = Display.Accent;
		canvas.FillCircle(last, 3.5f);
	}
}
