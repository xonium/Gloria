using System.Collections.ObjectModel;
using Gloria.Data.Entities;
using Gloria.Data.Queries;
using Gloria.ViewModels;

namespace Gloria.Services;

/// <summary>The tracked players and which one is being viewed. Every screen shows only the current profile's data.</summary>
public sealed class ProfileState(GloriaReadModel readModel) : ObservableObject
{
	private const string CurrentKey = "current-profile";
	private ProfileRecord? _current;

	public ObservableCollection<ProfileRecord> Profiles { get; } = [];

	public ProfileRecord? Current
	{
		get => _current;
		set
		{
			if (!Set(ref _current, value))
				return;

			if (value is not null)
				Preferences.Default.Set(CurrentKey, value.SteamId.ToString());
			CurrentChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	public ulong SteamId => Current?.SteamId ?? 0;

	public event EventHandler? CurrentChanged;

	public async Task LoadAsync()
	{
		var profiles = await readModel.ProfilesAsync();
		Profiles.Clear();
		foreach (var p in profiles)
			Profiles.Add(p);

		var saved = Preferences.Default.Get(CurrentKey, "");
		Current = Profiles.FirstOrDefault(p => p.SteamId.ToString() == saved) ?? Profiles.FirstOrDefault();
	}

	public async Task SelectOrAddAsync(ulong steamId, string name)
	{
		var profile = await readModel.AddProfileAsync(steamId, name);
		var existing = Profiles.FirstOrDefault(p => p.SteamId == steamId);
		if (existing is null)
		{
			Profiles.Add(profile);
			existing = profile;
		}

		Current = existing;
	}
}
