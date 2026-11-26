using System.Collections.ObjectModel;
using Gloria.Data;
using Gloria.Data.Entities;
using Gloria.Services;

namespace Gloria.ViewModels;

/// <summary>
/// The tracked profiles added to a page to compare with the viewed one, saved under a setting key: who is shown,
/// who can still be added, and the chips that name them in their line colors.
/// </summary>
public sealed class ComparedPlayers
{
	/// <summary>Players on a page at once, the viewed profile included: one per color.</summary>
	public static readonly int Max = Display.SeriesColors.Count;

	private readonly AppSettings _settings;
	private readonly ProfileState _profiles;
	private readonly string _key;
	private readonly List<ulong> _added;
	private readonly List<ProfileRecord> _addable = [];

	public ComparedPlayers(AppSettings settings, ProfileState profiles, string settingKey)
	{
		_settings = settings;
		_profiles = profiles;
		_key = settingKey;
		_added = (_settings.Get(_key) ?? "")
			.Split(',', StringSplitOptions.RemoveEmptyEntries)
			.Select(s => ulong.TryParse(s, out var id) ? id : 0)
			.Where(id => id != 0)
			.Distinct()
			.ToList();
	}

	/// <summary>Whether another tracked profile can be added, as of the last <see cref="Select"/>.</summary>
	public bool CanAdd => _addable.Count > 0;

	/// <summary>The viewed profile, then the added ones still tracked, up to <see cref="Max"/>.</summary>
	public List<ProfileRecord> Select(ProfileRecord current)
	{
		var players = new List<ProfileRecord> { current };
		foreach (var id in _added)
		{
			if (id != current.SteamId && _profiles.Profiles.FirstOrDefault(p => p.SteamId == id) is { } p && players.Count < Max)
				players.Add(p);
		}

		_addable.Clear();
		if (players.Count < Max)
			_addable.AddRange(_profiles.Profiles.Where(p => players.All(s => s.SteamId != p.SteamId)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase));
		return players;
	}

	/// <summary>Asks which tracked profile to add and saves it; false when nothing was added.</summary>
	public async Task<bool> AskToAddAsync()
	{
		if (_addable.Count == 0 || Shell.Current?.CurrentPage is not { } page)
			return false;

		// The sheet answers with the label, so two profiles with the same name get their SteamID added.
		var candidates = _addable.ToList();
		var labels = candidates
			.Select(p => candidates.Count(o => o.Name == p.Name) > 1 ? $"{p.Name} ({p.SteamId})" : p.Name)
			.ToList();
		var choice = await page.DisplayActionSheetAsync("Add a player to compare", "Cancel", null, [.. labels]);
		var index = choice is null ? -1 : labels.IndexOf(choice);
		if (index < 0)
			return false;

		if (!_added.Contains(candidates[index].SteamId))
			_added.Add(candidates[index].SteamId);
		Save();
		return true;
	}

	public void Remove(ulong id)
	{
		_added.Remove(id);
		Save();
	}

	/// <summary>
	/// Fills <paramref name="chips"/> with one chip per player in their color, or none with one player.
	/// Every player but the viewed profile can be removed.
	/// </summary>
	/// <param name="detail">The text after a player's name, by their index.</param>
	/// <param name="removed">Runs after a player was removed, e.g. to refresh the page.</param>
	public void FillChips(ObservableCollection<PlayerChip> chips, IReadOnlyList<ProfileRecord> players, Func<int, string> detail, Func<Task> removed)
	{
		chips.Clear();
		if (players.Count == 1)
			return;

		for (var i = 0; i < players.Count; i++)
		{
			var id = players[i].SteamId;
			chips.Add(new PlayerChip(
				players[i].Name,
				detail(i),
				Display.SeriesColors[i],
				CanRemove: i > 0,
				new Command(async () =>
				{
					Remove(id);
					await removed();
				})));
		}
	}

	private void Save() => _settings.Set(_key, _added.Count == 0 ? null : string.Join(',', _added));
}
