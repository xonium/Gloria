namespace Gloria.Services;

/// <summary>
/// Development aids, driven by environment variables so they never trigger in normal use:
/// <list type="bullet">
/// <item>GLORIA_START_ROUTE opens a screen at startup, e.g. <c>encounter?id=12</c> or <c>//matches</c>.</item>
/// <item>GLORIA_CAPTURE saves a PNG of that screen after it has rendered; GLORIA_CAPTURE_SHELL=1 includes the sidebar.
/// Desktop screenshot tools see WinUI content as black, so this is how to check the UI from a script.</item>
/// </list>
/// Combine with GLORIA_DATA_DIR to run against a scratch database.
/// </summary>
public static class DevHooks
{
	public static async Task RunAsync(Shell shell)
	{
		if (Environment.GetEnvironmentVariable("GLORIA_START_ROUTE") is { Length: > 0 } route)
			await shell.GoToAsync(route);

		if (Environment.GetEnvironmentVariable("GLORIA_CAPTURE") is not { Length: > 0 } path)
			return;

		if (shell.Window is { } window)
		{
			window.Width = 1440;
			window.Height = 1000;
		}

		await Task.Delay(4000);
		VisualElement target = Environment.GetEnvironmentVariable("GLORIA_CAPTURE_SHELL") is { Length: > 0 } || shell.CurrentPage is null
			? shell
			: shell.CurrentPage;
		if (await target.CaptureAsync() is { } shot)
		{
			await using var file = File.Create(path);
			await shot.CopyToAsync(file);
		}
	}
}
