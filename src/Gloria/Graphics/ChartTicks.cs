namespace Gloria.Graphics;

/// <summary>Axis ticks at round numbers, shared by the charts.</summary>
public static class ChartTicks
{
	/// <summary>About <paramref name="target"/> ticks between <paramref name="min"/> and <paramref name="max"/>, at steps of 1, 2, 2.5 or 5 times a power of ten.</summary>
	public static IEnumerable<float> Nice(float min, float max, int target)
	{
		var raw = (max - min) / target;
		if (raw <= 0)
			yield break;

		var magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(raw)));
		var step = new[] { 1f, 2f, 2.5f, 5f, 10f }.Select(m => m * magnitude).First(s => s >= raw);
		for (var v = MathF.Ceiling(min / step) * step; v <= max + step * 0.001f; v += step)
			yield return MathF.Round(v / step) * step;
	}
}
