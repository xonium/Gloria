using System.Globalization;
using System.Text;
using Gloria.Data;
using Gloria.ViewModels;

namespace Gloria.Services;

/// <summary>
/// Records a scripted scene as numbered PNG frames for the README's GIFs (<c>build/make-gifs.ps1</c>). Desktop recorders
/// see WinUI content as black, so frames come from <see cref="VisualElement.CaptureAsync"/>. Each scene steps the app
/// frame by frame instead of capturing while it plays, so the GIF is smooth however long a capture takes. Writes
/// <c>frames.txt</c>, an ffmpeg concat list with each frame's duration, and quits the app when done.
/// </summary>
public sealed class DevRecorder
{
	private const double FrameSeconds = 1 / 15.0;

	/// <summary>Settings the scenes change by selecting options; put back afterwards so recording leaves no trace.</summary>
	private static readonly string[] TouchedSettings = ["compare-target", "compare-weapon"];

	private readonly Shell _shell;
	private readonly string _folder;
	private readonly List<(string File, double Seconds)> _frames = [];

	private DevRecorder(Shell shell, string folder)
	{
		_shell = shell;
		_folder = folder;
	}

	public static async Task RunAsync(Shell shell, string folder, string scene)
	{
		Directory.CreateDirectory(folder);
		foreach (var old in Directory.EnumerateFiles(folder, "*.png").Concat(Directory.EnumerateFiles(folder, "*.txt")))
			File.Delete(old);

		var settings = shell.Handler?.MauiContext?.Services.GetService<AppSettings>();
		var saved = TouchedSettings.ToDictionary(k => k, k => settings?.Get(k));
		var recorder = new DevRecorder(shell, folder);
		try
		{
			await (scene switch
			{
				"replay" => recorder.ReplayAsync(),
				"overview" => recorder.OverviewAsync(),
				"compare" => recorder.CompareAsync(),
				_ => throw new ArgumentException($"Unknown scene '{scene}'. Use replay, overview or compare."),
			});
			recorder.WriteList();
		}
		catch (Exception ex)
		{
			await File.WriteAllTextAsync(Path.Combine(folder, "error.txt"), ex.ToString());
		}
		finally
		{
			foreach (var (key, value) in saved)
				settings?.Set(key, value);
			Application.Current?.Quit();
		}
	}

	/// <summary>The encounter's replay card, from just before first sighting to the end, at half speed.</summary>
	private async Task ReplayAsync()
	{
		await SettleAsync(1440, 1000);
		if (_shell.CurrentPage is not { BindingContext: EncounterViewModel vm } page || page.FindByName<VisualElement>("ReplayCard") is not { } card)
			throw new InvalidOperationException("Open an encounter first: GLORIA_START_ROUTE=encounter?id=N.");

		var tickRate = vm.TickRate;
		double position = Math.Max(0, vm.SightingIndex - tickRate);
		var step = tickRate * 0.5 * FrameSeconds;
		vm.Position = position;
		await Task.Delay(300);
		await CaptureAsync(card, 0.8);
		vm.ShowPlaying(true);
		while (position < vm.MaxPosition)
		{
			position = Math.Min(position + step, vm.MaxPosition);
			vm.Position = position;
			await Task.Delay(60);
			if (position < vm.MaxPosition)
				await CaptureAsync(card, FrameSeconds);
		}

		vm.ShowPlaying(false);
		await Task.Delay(100);
		await CaptureAsync(card, 2.5);
	}

	/// <summary>The Overview with the sidebar: down past the habits to the recent matches, and back up.</summary>
	private async Task OverviewAsync()
	{
		await SettleAsync(1280, 860);
		if (_shell.CurrentPage is not { BindingContext: OverviewViewModel })
			throw new InvalidOperationException("Open the Overview first: GLORIA_START_ROUTE=//overview.");

		await CaptureAsync(_shell, 2.5);
		await ScrollAsync(0.5, 1.2, 2);
		await ScrollAsync(1.0, 1.2, 2.5);
		await ScrollAsync(0, 0.8, 0.5);
	}

	/// <summary>The Compare page against the bundled pro: a few weapon classes, then down the page and back up.</summary>
	private async Task CompareAsync()
	{
		await SettleAsync(1280, 860);
		if (_shell.CurrentPage is not { BindingContext: CompareViewModel vm })
			throw new InvalidOperationException("Open Compare first: GLORIA_START_ROUTE=//compare.");

		var pro = vm.TargetNames.ToList().FindIndex(n => n.Contains("donk", StringComparison.OrdinalIgnoreCase));
		if (pro >= 0 && vm.SelectedTargetIndex != pro)
		{
			vm.SelectedTargetIndex = pro;
			await Task.Delay(2500);
		}

		await CaptureAsync(_shell, 2);
		// The options are rebuilt on every refresh, so look each one up again by its value.
		var classes = vm.Weapons.Where(w => w.Value is not null).Take(2).Select(w => w.Value).ToList();
		foreach (var value in classes.Append(null))
		{
			if (vm.Weapons.FirstOrDefault(w => w.Value == value) is { } option)
				await SelectAsync(option, value is null ? 1.2 : 1.6);
		}

		await ScrollAsync(0.5, 1.2, 2.2);
		await ScrollAsync(1.0, 1.0, 2.5);
		await ScrollAsync(0, 0.8, 0.5);
	}

	private async Task SettleAsync(double width, double height)
	{
		if (_shell.Window is { } window)
		{
			window.Width = width;
			window.Height = height;
		}

		await Task.Delay(4000);
	}

	private async Task SelectAsync(ChoiceOption option, double hold)
	{
		option.SelectCommand.Execute(null);
		await Task.Delay(1500);
		await CaptureAsync(_shell, hold);
	}

	/// <summary>Eases the page's scroll to <paramref name="fraction"/> of its height, a frame per step, then holds.</summary>
	private async Task ScrollAsync(double fraction, double seconds, double hold)
	{
		if (_shell.CurrentPage is not ContentPage { Content: ScrollView scroll })
			return;

		var from = scroll.ScrollY;
		var to = Math.Max(0, scroll.ContentSize.Height - scroll.Height) * fraction;
		var steps = Math.Max(1, (int)Math.Round(seconds / FrameSeconds));
		for (var k = 1; k <= steps; k++)
		{
			var t = k / (double)steps;
			var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
			await scroll.ScrollToAsync(0, from + (to - from) * eased, animated: false);
			await Task.Delay(100);
			await CaptureAsync(_shell, k == steps ? hold : FrameSeconds);
		}
	}

	private async Task CaptureAsync(VisualElement target, double seconds)
	{
		if (await target.CaptureAsync() is not { } shot)
			throw new InvalidOperationException("The capture returned nothing.");

		var name = $"{_frames.Count:0000}.png";
		await using (var file = File.Create(Path.Combine(_folder, name)))
			await shot.CopyToAsync(file);
		_frames.Add((name, seconds));
	}

	private void WriteList()
	{
		var list = new StringBuilder();
		foreach (var (file, seconds) in _frames)
			list.Append(CultureInfo.InvariantCulture, $"file '{file}'\nduration {seconds:0.####}\n");
		// The concat demuxer ignores the last duration unless the last file is listed again.
		list.Append(CultureInfo.InvariantCulture, $"file '{_frames[^1].File}'\n");
		File.WriteAllText(Path.Combine(_folder, "frames.txt"), list.ToString());
	}
}
