using Gloria.Services;

namespace Gloria.Controls;

/// <summary>Takes demos by drag and drop, or by a file picker on click. Folders dropped on it are expanded to the demos inside.</summary>
public partial class DemoDropZone : ContentView
{
	public DemoDropZone() => InitializeComponent();

	/// <summary>Raised with the chosen .dem paths; never with an empty list.</summary>
	public event EventHandler<IReadOnlyList<string>>? DemosChosen;

	private async void OnPickFiles(object? sender, TappedEventArgs e) =>
		Raise(await DemoSources.PickFilesAsync());

	private void OnDragOver(object? sender, DragEventArgs e) => DemoSources.AcceptDrag(e);

	private async void OnDrop(object? sender, DropEventArgs e) =>
		Raise(await DemoSources.DroppedDemosAsync(e));

	private void Raise(IReadOnlyList<string> paths)
	{
		if (paths.Count > 0)
			DemosChosen?.Invoke(this, paths);
	}
}
