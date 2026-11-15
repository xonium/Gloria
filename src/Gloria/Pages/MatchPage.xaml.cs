using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class MatchPage : ContentPage
{
	private readonly MatchViewModel _viewModel;
	private bool _appeared;

	public MatchPage(MatchViewModel viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
		BindingContext = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		// The first time, the query attributes load the match; after that, flags may have changed meanwhile.
		if (_appeared)
			await _viewModel.ReloadAsync();
		_appeared = true;
	}
}
