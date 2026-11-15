using System.Collections.ObjectModel;
using System.Windows.Input;
using Gloria.Data;
using Gloria.Data.Queries;
using Gloria.Services;

namespace Gloria.ViewModels;

public sealed class MatchesViewModel : ObservableObject
{
	private readonly GloriaReadModel _readModel;
	private readonly ProfileState _profiles;
	private readonly ImportQueue _queue;
	private string _subtitle = "";

	public MatchesViewModel(GloriaReadModel readModel, ProfileState profiles, ImportQueue queue, AppSettings settings)
	{
		_readModel = readModel;
		_profiles = profiles;
		_queue = queue;
		AddFolderCommand = new Command(async () =>
		{
			if (await DemoSources.PickFolderAsync() is { } folder)
			{
				settings.AddDemoFolder(folder);
				await _queue.EnqueueFolderAsync(folder);
			}
		});
		ImportFilesCommand = new Command(async () => await _queue.EnqueueAsync(await DemoSources.PickFilesAsync()));
		ClearFinishedCommand = new Command(() =>
		{
			foreach (var item in _queue.Items.Where(i => i.IsFinished).ToList())
				_queue.Items.Remove(item);
			OnPropertyChanged(nameof(HasQueue));
		});
		_queue.DemoImported += async (_, _) => await RefreshAsync();
		_queue.Changed += (_, _) => OnPropertyChanged(nameof(HasQueue));
		_profiles.CurrentChanged += async (_, _) => await RefreshAsync();
	}

	public ObservableCollection<ImportItem> Queue => _queue.Items;

	public ObservableCollection<MatchRow> Matches { get; } = [];

	public ICommand AddFolderCommand { get; }

	public ICommand ImportFilesCommand { get; }

	public ICommand ClearFinishedCommand { get; }

	public bool HasQueue => _queue.Items.Count > 0;

	public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

	public async Task RefreshAsync()
	{
		if (_profiles.Current is not { } profile)
			return;

		var matches = await _readModel.MatchesAsync(profile.SteamId);
		var total = await _readModel.DemoCountAsync();
		Subtitle = $"{total} demos imported · {profile.Name} is in {matches.Count} of them";
		Matches.Clear();
		foreach (var m in matches)
			Matches.Add(MatchRow.From(m));
		OnPropertyChanged(nameof(HasQueue));
	}
}
