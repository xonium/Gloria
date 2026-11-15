using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class EncounterPage : ContentPage
{
	private readonly EncounterViewModel _viewModel;

	public EncounterPage(EncounterViewModel viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
		BindingContext = viewModel;
		viewModel.Redraw += (_, _) =>
		{
			MapView.Invalidate();
			ReplayView.Invalidate();
			ErrorView.Invalidate();
			SpeedView.Invalidate();
		};
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_viewModel.Stop();
	}
}
