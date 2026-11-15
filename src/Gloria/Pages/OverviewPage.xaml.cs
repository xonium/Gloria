using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class OverviewPage : ContentPage
{
	private readonly OverviewViewModel _viewModel;

	public OverviewPage(OverviewViewModel viewModel)
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
