using System.Text.RegularExpressions;

namespace Gloria.Services;

/// <summary>Picks demo files and folders, and finds where CS2 keeps downloaded demos.</summary>
public static partial class DemoSources
{
	private static readonly FilePickerFileType DemoFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
	{
		[DevicePlatform.WinUI] = [".dem"],
	});

	public static async Task<IReadOnlyList<string>> PickFilesAsync()
	{
		var files = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "Choose demos", FileTypes = DemoFileType });
		return files?.Where(f => f is not null).Select(f => f!.FullPath).ToList() ?? [];
	}

	public static async Task<string?> PickFolderAsync()
	{
#if WINDOWS
		var picker = new Windows.Storage.Pickers.FolderPicker();
		picker.FileTypeFilter.Add("*");
		var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow;
		if (window is null)
			return null;

		WinRT.Interop.InitializeWithWindow.Initialize(picker, window.WindowHandle);
		var folder = await picker.PickSingleFolderAsync();
		return folder?.Path;
#else
		await Task.CompletedTask;
		return null;
#endif
	}

	/// <summary>The CS2 replays folder in every Steam library that has one.</summary>
	public static IReadOnlyList<string> FindCs2ReplayFolders()
	{
#if WINDOWS
		var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
		if (string.IsNullOrEmpty(steam))
			return [];

		var libraries = new List<string> { steam };
		var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
		if (File.Exists(vdf))
		{
			libraries.AddRange(LibraryPath().Matches(File.ReadAllText(vdf)).Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
		}

		return libraries
			.Select(l => Path.GetFullPath(Path.Combine(l, "steamapps", "common", "Counter-Strike Global Offensive", "game", "csgo", "replays")))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(Directory.Exists)
			.ToList();
#else
		return [];
#endif
	}

	/// <summary>Files and folders dropped on the window, with folders expanded to the demos inside them.</summary>
	public static async Task<IReadOnlyList<string>> DroppedDemosAsync(DropEventArgs e)
	{
#if WINDOWS
		var view = e.PlatformArgs?.DragEventArgs.DataView;
		if (view is null || !view.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
			return [];

		var paths = new List<string>();
		foreach (var item in await view.GetStorageItemsAsync())
		{
			if (Directory.Exists(item.Path))
				paths.AddRange(Directory.EnumerateFiles(item.Path, "*.dem", SearchOption.AllDirectories));
			else if (item.Path.EndsWith(".dem", StringComparison.OrdinalIgnoreCase))
				paths.Add(item.Path);
		}

		return paths;
#else
		await Task.CompletedTask;
		return [];
#endif
	}

	public static void AcceptDrag(DragEventArgs e)
	{
#if WINDOWS
		if (e.PlatformArgs?.DragEventArgs is { } args)
			args.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
#endif
		e.AcceptedOperation = DataPackageOperation.Copy;
	}

	[GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
	private static partial Regex LibraryPath();
}
