using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class MatchesPage : ContentPage
{
	private readonly MatchesViewModel _viewModel;

	public MatchesPage(MatchesViewModel viewModel)
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
