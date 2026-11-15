using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class ImportPage : ContentPage
{
	private readonly ImportViewModel _viewModel;

	public ImportPage(ImportViewModel viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
		BindingContext = viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.Refresh();
	}

	private async void OnDemosChosen(object? sender, IReadOnlyList<string> files) =>
		await _viewModel.ImportFilesAsync(files);
}
