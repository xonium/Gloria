using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class SetupPage : ContentPage
{
	private readonly SetupViewModel _viewModel;

	public SetupPage(SetupViewModel viewModel)
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
