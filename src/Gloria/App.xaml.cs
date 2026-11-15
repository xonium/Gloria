namespace Gloria;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		_services = services;
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;
	}

	// The shell is resolved here, not injected, so its XAML can use the app's resources loaded above.
	protected override Window CreateWindow(IActivationState? activationState) =>
		new(_services.GetRequiredService<AppShell>()) { Title = "Gloria", MinimumWidth = 960, MinimumHeight = 640 };
}
