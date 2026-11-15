using Gloria.Services;

namespace Gloria.Graphics;

/// <summary>A single-series line with a dot on the latest value. Values are drawn oldest to newest.</summary>
public sealed class SparklineDrawable : IDrawable
{
	public IReadOnlyList<double> Values { get; set; } = [];

	public void Draw(ICanvas canvas, RectF rect)
	{
		var values = Values.Where(v => !double.IsNaN(v)).ToList();
		if (values.Count < 2)
			return;

		var min = values.Min();
		var max = values.Max();
		var span = max - min < 1e-9 ? 1 : max - min;
		var inset = rect.Inflate(-4, -4);
		PointF At(int i) => new(
			inset.Left + inset.Width * i / (values.Count - 1),
			(float)(inset.Bottom - (values[i] - min) / span * inset.Height));

		var path = new PathF(At(0));
		for (var i = 1; i < values.Count; i++)
			path.LineTo(At(i));

		canvas.StrokeColor = Display.Accent;
		canvas.StrokeSize = 2;
		canvas.StrokeLineJoin = LineJoin.Round;
		canvas.DrawPath(path);
		var last = At(values.Count - 1);
		canvas.FillColor = Display.Accent;
		canvas.FillCircle(last, 3.5f);
	}
}
