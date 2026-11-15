using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class PlayerPage : ContentPage
{
	private readonly PlayerViewModel _viewModel;

	public PlayerPage(PlayerViewModel viewModel)
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
