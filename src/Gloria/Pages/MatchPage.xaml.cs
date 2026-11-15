using Gloria.ViewModels;

namespace Gloria.Pages;

public partial class MatchPage : ContentPage
{
	public MatchPage(MatchViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
