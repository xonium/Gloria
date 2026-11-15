using System.Windows.Input;
using Gloria.Data;
using Gloria.Services;

namespace Gloria.ViewModels;

/// <summary>One option of a segmented control, e.g. a period. <see cref="Value"/> null stands for "all".</summary>
public sealed class ChoiceOption : ObservableObject
{
	private bool _selected;

	public ChoiceOption(string label, int? value, Action<ChoiceOption> select)
	{
		Label = label;
		Value = value;
		SelectCommand = new Command(() => select(this));
	}

	public string Label { get; }

	public int? Value { get; }

	public ICommand SelectCommand { get; }

	public bool IsSelected
	{
		get => _selected;
		set
		{
			if (Set(ref _selected, value))
			{
				OnPropertyChanged(nameof(Background));
				OnPropertyChanged(nameof(Foreground));
			}
		}
	}

	public Color Background => IsSelected ? Display.Line : Colors.Transparent;

	public Color Foreground => IsSelected ? Display.Text : Display.Text2;

	/// <summary>The period saved under <paramref name="key"/>: a number of demos, null for all, or <paramref name="fallback"/> if none was saved.</summary>
	public static int? LoadPeriod(AppSettings settings, string key, int? fallback) => settings.Get(key) switch
	{
		null => fallback,
		"all" => null,
		var saved => int.TryParse(saved, out var n) && n > 0 ? n : fallback,
	};

	public static void SavePeriod(AppSettings settings, string key, int? period) => settings.Set(key, period?.ToString() ?? "all");

	/// <summary>Demo-count periods ("Last 10", …, "All" for null), with <paramref name="selected"/> selected.</summary>
	public static IReadOnlyList<ChoiceOption> Periods(int?[] values, int? selected, Action<ChoiceOption> select) =>
		values.Select(v => new ChoiceOption(v is { } n ? $"Last {n}" : "All", v, select) { IsSelected = v == selected }).ToList();
}
