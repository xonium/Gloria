using System.Collections.ObjectModel;
using Gloria.Data.Import;
using Gloria.ViewModels;

namespace Gloria.Services;

/// <summary>One demo waiting for, or going through, import.</summary>
public sealed class ImportItem(string path) : ObservableObject
{
	private ImportStage _stage = ImportStage.Queued;
	private double _progress;
	private string _status = "Waiting";

	public string Path { get; } = path;

	public string FileName { get; } = System.IO.Path.GetFileName(path);

	public string SizeText { get; } = File.Exists(path) ? $"{new FileInfo(path).Length / 1048576} MB" : "";

	public ImportStage Stage
	{
		get => _stage;
		set
		{
			if (Set(ref _stage, value))
			{
				OnPropertyChanged(nameof(IsFinished));
				OnPropertyChanged(nameof(IsRunning));
			}
		}
	}

	/// <summary>Overall progress 0..1, with parsing taking most of the bar.</summary>
	public double Progress { get => _progress; set => Set(ref _progress, value); }

	public string Status { get => _status; set => Set(ref _status, value); }

	public bool IsFinished => Stage is ImportStage.Done or ImportStage.Skipped or ImportStage.Failed;

	public bool IsRunning => !IsFinished && Stage != ImportStage.Queued;
}

/// <summary>
/// Imports demos one at a time on a background thread and reports progress on the UI thread.
/// Files already imported from the same path and size are not queued again, so rescanning a folder is cheap.
/// </summary>
public sealed class ImportQueue(DemoImporter importer, Data.Queries.GloriaReadModel readModel)
{
	private readonly Lock _gate = new();
	private bool _running;

	public ObservableCollection<ImportItem> Items { get; } = [];

	public int Remaining => Items.Count(i => !i.IsFinished);

	/// <summary>Raised on the UI thread after each demo is stored.</summary>
	public event EventHandler<ImportResult>? DemoImported;

	/// <summary>Raised on the UI thread when the queue changes or progresses.</summary>
	public event EventHandler? Changed;

	public async Task EnqueueAsync(IEnumerable<string> paths)
	{
		var known = await readModel.ImportedFilesAsync();
		foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var info = new FileInfo(path);
			if (!info.Exists
				|| known.Contains((info.FullName, info.Length))
				|| Items.Any(i => !i.IsFinished && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			Items.Add(new ImportItem(info.FullName));
		}

		Changed?.Invoke(this, EventArgs.Empty);
		lock (_gate)
		{
			if (_running)
				return;
			_running = true;
		}

		_ = Task.Run(ProcessAsync);
	}

	public async Task EnqueueFolderAsync(string folder) =>
		await EnqueueAsync(Directory.Exists(folder)
			? Directory.EnumerateFiles(folder, "*.dem", SearchOption.AllDirectories)
			: []);

	private async Task ProcessAsync()
	{
		while (true)
		{
			var item = await MainThread.InvokeOnMainThreadAsync(() => Items.FirstOrDefault(i => i.Stage == ImportStage.Queued));
			if (item is null)
			{
				lock (_gate)
					_running = false;
				return;
			}

			var last = -1.0;
			var progress = new Progress(p =>
			{
				var overall = Overall(p);
				if (p.Stage == ImportStage.Parsing && overall - last < 0.01)
					return;
				last = overall;
				MainThread.BeginInvokeOnMainThread(() =>
				{
					item.Stage = p.Stage;
					item.Progress = overall;
					item.Status = p.Stage switch
					{
						ImportStage.Hashing => "Checking if it is new",
						ImportStage.Parsing => $"Reading the demo {p.Fraction:P0}",
						ImportStage.Analyzing => "Finding encounters",
						ImportStage.Saving => "Saving",
						_ => item.Status,
					};
					Changed?.Invoke(this, EventArgs.Empty);
				});
			});

			var result = await importer.ImportAsync(item.Path, progress);
			if (result.Outcome == ImportStage.Failed)
				LogFailure(item.Path, result);
			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				item.Stage = result.Outcome;
				item.Progress = 1;
				item.Status = result.Outcome switch
				{
					ImportStage.Done => $"{result.Encounters} encounters · {result.Elapsed.TotalSeconds:0.0} s",
					ImportStage.Skipped => result.Message ?? "Already imported",
					_ => $"Could not read: {result.Message}",
				};
				Changed?.Invoke(this, EventArgs.Empty);
				if (result.Outcome == ImportStage.Done)
					DemoImported?.Invoke(this, result);
			});
		}
	}

	/// <summary>
	/// Keeps failures in <c>import-errors.log</c> in the data folder: the queue's status line is gone once the app
	/// closes, and a demo that failed then is simply missing.
	/// </summary>
	private static void LogFailure(string path, ImportResult result)
	{
		try
		{
			Directory.CreateDirectory(Data.DatabaseLocation.DataDirectory);
			File.AppendAllText(
				System.IO.Path.Combine(Data.DatabaseLocation.DataDirectory, "import-errors.log"),
				$"{DateTimeOffset.Now:O} {path}{Environment.NewLine}{result.Error ?? result.Message}{Environment.NewLine}{Environment.NewLine}");
		}
		catch (IOException)
		{
		}
	}

	private static double Overall(ImportProgress p) => p.Stage switch
	{
		ImportStage.Hashing => 0.02,
		ImportStage.Parsing => 0.05 + 0.85 * p.Fraction,
		ImportStage.Analyzing => 0.92,
		ImportStage.Saving => 0.97,
		_ => 1,
	};

	private sealed class Progress(Action<ImportProgress> report) : IProgress<ImportProgress>
	{
		public void Report(ImportProgress value) => report(value);
	}
}
