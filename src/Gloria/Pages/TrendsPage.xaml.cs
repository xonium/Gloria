using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class TrendsPage : ContentPage
{
	private readonly TrendsViewModel _viewModel;

	public TrendsPage(TrendsViewModel viewModel)
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
