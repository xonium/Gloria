using Gloria.Data;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Gloria.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();
		// WinUI reports unhandled exceptions only as a native crash code; keep the managed details.
		UnhandledException += (_, e) => LogCrash(e.Exception);
		AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
		TaskScheduler.UnobservedTaskException += (_, e) => LogCrash(e.Exception);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	private static void LogCrash(Exception? exception)
	{
		try
		{
			Directory.CreateDirectory(DatabaseLocation.DataDirectory);
			File.AppendAllText(Path.Combine(DatabaseLocation.DataDirectory, "crash.log"), $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
		}
		catch (IOException)
		{
		}
	}
}
