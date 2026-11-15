using System.Collections.ObjectModel;
using System.Windows.Input;
using Gloria.Data;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed class FolderItem(string path, Action<string> remove)
{
	public string Path { get; } = path;

	public Command RemoveCommand { get; } = new(() => remove(path));
}

/// <summary>Adds demos by drag and drop, file picker or saved folders, and shows the import queue.</summary>
public sealed class ImportViewModel : ObservableObject
{
	private readonly ImportQueue _queue;
	private readonly AppSettings _settings;
	private string _message = "";

	public ImportViewModel(ImportQueue queue, AppSettings settings)
	{
		_queue = queue;
		_settings = settings;
		ImportFilesCommand = new Command(async () => await ImportFilesAsync(await DemoSources.PickFilesAsync()));
		AddFolderCommand = new Command(async () =>
		{
			if (await DemoSources.PickFolderAsync() is { } folder)
				await AddFoldersAsync([folder]);
		});
		UseReplaysCommand = new Command(async () =>
		{
			var folders = DemoSources.FindCs2ReplayFolders();
			if (folders.Count == 0)
				Message = "Couldn't find a CS2 replays folder on this PC. Add the folder yourself.";
			else
				await AddFoldersAsync(folders);
		});
		RescanCommand = new Command(async () => await QueueNewAsync(
			async () =>
			{
				foreach (var folder in _settings.DemoFolders)
					await _queue.EnqueueFolderAsync(folder);
			},
			"No new demos in your folders."));
		ClearFinishedCommand = new Command(() =>
		{
			foreach (var item in _queue.Items.Where(i => i.IsFinished).ToList())
				_queue.Items.Remove(item);
			OnQueueChanged();
		});
		_queue.Changed += (_, _) => OnQueueChanged();
		Folders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoFolders));
	}

	public ObservableCollection<FolderItem> Folders { get; } = [];

	public ObservableCollection<ImportItem> Queue => _queue.Items;

	public bool HasNoFolders => Folders.Count == 0;

	public bool HasQueue => _queue.Items.Count > 0;

	public bool HasNoQueue => !HasQueue;

	public ICommand ImportFilesCommand { get; }

	public ICommand AddFolderCommand { get; }

	public ICommand UseReplaysCommand { get; }

	public ICommand RescanCommand { get; }

	public ICommand ClearFinishedCommand { get; }

	public string Message
	{
		get => _message;
		private set
		{
			if (Set(ref _message, value))
				OnPropertyChanged(nameof(HasMessage));
		}
	}

	public bool HasMessage => Message.Length > 0;

	public void Refresh()
	{
		Folders.Clear();
		foreach (var folder in _settings.DemoFolders)
			Folders.Add(new FolderItem(folder, RemoveFolder));
		Message = "";
		OnQueueChanged();
	}

	public Task ImportFilesAsync(IReadOnlyList<string> files) =>
		files.Count == 0
			? Task.CompletedTask
			: QueueNewAsync(() => _queue.EnqueueAsync(files), "Those demos are already imported.");

	private async Task AddFoldersAsync(IReadOnlyList<string> folders)
	{
		foreach (var folder in folders)
			_settings.AddDemoFolder(folder);
		Refresh();
		await QueueNewAsync(
			async () =>
			{
				foreach (var folder in folders)
					await _queue.EnqueueFolderAsync(folder);
			},
			"No new demos in that folder. Demos that land there later are picked up at startup, or with Rescan folders.");
	}

	/// <summary>Runs an enqueue and says so when it added nothing.</summary>
	private async Task QueueNewAsync(Func<Task> enqueue, string nothingQueued)
	{
		var before = _queue.Items.Count;
		await enqueue();
		Message = _queue.Items.Count == before ? nothingQueued : "";
	}

	private void OnQueueChanged()
	{
		OnPropertyChanged(nameof(HasQueue));
		OnPropertyChanged(nameof(HasNoQueue));
	}

	private void RemoveFolder(string folder)
	{
		_settings.RemoveDemoFolder(folder);
		var item = Folders.FirstOrDefault(f => f.Path == folder);
		if (item is not null)
			Folders.Remove(item);
	}
}
