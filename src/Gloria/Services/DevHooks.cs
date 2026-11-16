namespace Gloria.Services;

/// <summary>
/// Development aids, driven by environment variables so they never trigger in normal use:
/// <list type="bullet">
/// <item>GLORIA_IMPORT imports a demo file or folder (paths separated by ';') and waits for it before anything else.</item>
/// <item>GLORIA_START_ROUTE opens a screen at startup, e.g. <c>encounter?id=12</c> or <c>//matches</c>.</item>
/// <item>GLORIA_CAPTURE saves a PNG of that screen after it has rendered; GLORIA_CAPTURE_SHELL=1 includes the sidebar.
/// Desktop screenshot tools see WinUI content as black, so this is how to check the UI from a script.</item>
/// <item>GLORIA_CAPTURE_SCROLL scrolls the page down by that many pixels before the capture, to check what is below the fold.</item>
/// <item>GLORIA_RECORD records the scene named by GLORIA_RECORD_SCENE (replay, overview or compare) as frames into that
/// folder, then quits; see <see cref="DevRecorder"/>.</item>
/// </list>
/// Combine with GLORIA_DATA_DIR to run against a scratch database.
/// </summary>
public static class DevHooks
{
	public static async Task RunAsync(Shell shell)
	{
		if (Environment.GetEnvironmentVariable("GLORIA_IMPORT") is { Length: > 0 } import
			&& shell.Handler?.MauiContext?.Services.GetService<ImportQueue>() is { } queue)
		{
			foreach (var item in import.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				if (Directory.Exists(item))
					await queue.EnqueueFolderAsync(item);
				else
					await queue.EnqueueAsync([item]);
			}

			while (queue.Remaining > 0)
				await Task.Delay(500);
		}

		if (Environment.GetEnvironmentVariable("GLORIA_START_ROUTE") is { Length: > 0 } route)
			await shell.GoToAsync(route);

		if (Environment.GetEnvironmentVariable("GLORIA_RECORD") is { Length: > 0 } folder)
		{
			await DevRecorder.RunAsync(shell, folder, Environment.GetEnvironmentVariable("GLORIA_RECORD_SCENE") ?? "");
			return;
		}

		if (Environment.GetEnvironmentVariable("GLORIA_CAPTURE") is not { Length: > 0 } path)
			return;

		if (shell.Window is { } window)
		{
			window.Width = 1440;
			window.Height = 1000;
		}

		await Task.Delay(4000);
		if (int.TryParse(Environment.GetEnvironmentVariable("GLORIA_CAPTURE_SCROLL"), out var scroll)
			&& shell.CurrentPage is ContentPage { Content: ScrollView scrollView })
		{
			await scrollView.ScrollToAsync(0, scroll, animated: false);
			await Task.Delay(500);
		}

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
