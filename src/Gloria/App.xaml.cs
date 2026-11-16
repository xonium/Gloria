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
	// The empty TitleBar hides the caption text (the sidebar already says Gloria); Title still names the
	// window in the taskbar and Alt+Tab.
	protected override Window CreateWindow(IActivationState? activationState) =>
		new(_services.GetRequiredService<AppShell>())
		{
			Title = "Gloria",
			TitleBar = new TitleBar(),
			MinimumWidth = 960,
			MinimumHeight = 640,
		};
}
