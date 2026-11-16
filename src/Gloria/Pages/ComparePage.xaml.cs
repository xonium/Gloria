using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class ComparePage : ContentPage
{
	private readonly CompareViewModel _viewModel;

	public ComparePage(CompareViewModel viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
		BindingContext = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.RefreshAsync();
	}
}
