using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class SprayPage : ContentPage
{
	private readonly SprayViewModel _viewModel;

	public SprayPage(SprayViewModel viewModel)
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
