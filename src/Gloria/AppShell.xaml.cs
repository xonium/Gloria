using Gloria.Pages;
using Gloria.Services;
using Gloria.ViewModels;

namespace Gloria;

public partial class AppShell : Shell
{
	private readonly ShellViewModel _viewModel;
	private bool _started;

	public AppShell(ShellViewModel viewModel, IServiceProvider services)
	{
		_viewModel = viewModel;
		InitializeComponent();
		BindingContext = viewModel;
		Routing.RegisterRoute("match", new ServiceRouteFactory<MatchPage>(services));
		Routing.RegisterRoute("encounter", new ServiceRouteFactory<EncounterPage>(services));
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_started)
			return;

		_started = true;
		await _viewModel.StartAsync();
		await DevHooks.RunAsync(this);
	}

	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);
		var route = CurrentState.Location.OriginalString;
		_viewModel.OnNavigated(route);
		// No sidebar until there is something to show in it.
		FlyoutBehavior = route.Contains("welcome", StringComparison.Ordinal) ? FlyoutBehavior.Disabled : FlyoutBehavior.Locked;
	}
}

/// <summary>Creates a route's page from the container; Shell's own factory needs a parameterless constructor.</summary>
internal sealed class ServiceRouteFactory<TPage>(IServiceProvider services) : RouteFactory
	where TPage : Page
{
	public override Element GetOrCreate() => services.GetRequiredService<TPage>();

	public override Element GetOrCreate(IServiceProvider _) => GetOrCreate();
}
