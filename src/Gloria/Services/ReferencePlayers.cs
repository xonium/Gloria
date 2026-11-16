using Gloria.Data.Import;
using Gloria.Data.Queries;
using Gloria.Data.References;
using Gloria.Ml;
using Microsoft.Extensions.Logging;

namespace Gloria.Services;

/// <summary>
/// The players bundled with the app to compare against (Resources/Raw/references, made by tools/Gloria.Trainer).
/// Their encounters are classified with the app's current aim model, the same one that judges the user's, and again
/// whenever that model is replaced.
/// </summary>
public sealed class ReferencePlayers
{
	private const string Folder = "references";

	private readonly SwitchableAimClassifier _classifier;
	private readonly string _analysisVersion;
	private readonly ILogger<ReferencePlayers> _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private IReadOnlyList<ReferencePlayer>? _players;
	private IReadOnlyList<ComparisonSide>? _sides;

	public ReferencePlayers(SwitchableAimClassifier classifier, DemoImporter importer, ILogger<ReferencePlayers> logger)
	{
		_classifier = classifier;
		_analysisVersion = importer.AnalysisVersionKey;
		_logger = logger;
		_classifier.Changed += (_, _) =>
		{
			_sides = null;
			Changed?.Invoke(this, EventArgs.Empty);
		};
	}

	/// <summary>Raised when the players' numbers change, because the aim model was replaced. Not on the UI thread.</summary>
	public event EventHandler? Changed;

	/// <summary>Every bundled player, classified with the current model. Loads them the first time.</summary>
	public async Task<IReadOnlyList<ComparisonSide>> AllAsync()
	{
		if (_sides is { } sides)
			return sides;

		await _gate.WaitAsync();
		try
		{
			_players ??= await LoadAsync();
			return _sides ??= await Task.Run(() => _players.Select(Side).ToList());
		}
		finally
		{
			_gate.Release();
		}
	}

	private ComparisonSide Side(ReferencePlayer player)
	{
		var records = player.ToRecords();
		if (records.Count > 0)
		{
			var source = _classifier.Source;
			var classifications = _classifier.Classify(records.Select(r => r.ToFeatures()).ToList());
			for (var i = 0; i < records.Count; i++)
				records[i].ApplyClassification(classifications[i], source);
		}

		var note = player.AnalysisVersion == _analysisVersion
			? player.Description
			: $"{player.Description} · measured by an older analysis, so some numbers may differ";
		return ComparisonSide.From(player.Name, ComparisonKind.Pro, player.Demos.Count, records, note);
	}

	private async Task<IReadOnlyList<ReferencePlayer>> LoadAsync()
	{
		var players = new List<ReferencePlayer>();
		try
		{
			IReadOnlyList<string> files;
			await using (var index = await FileSystem.OpenAppPackageFileAsync($"{Folder}/{ReferencePlayer.IndexFileName}"))
				files = ReferencePlayer.ReadIndex(index);

			foreach (var file in files)
			{
				try
				{
					await using var stream = await FileSystem.OpenAppPackageFileAsync($"{Folder}/{file}");
					var player = await Task.Run(() => ReferencePlayer.Read(stream));
					if (player.Format > ReferencePlayer.CurrentFormat)
						_logger.LogWarning("Reference {File} has format {Format}, newer than this app reads", file, player.Format);
					else
						players.Add(player);
				}
				catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
				{
					_logger.LogWarning(ex, "Reference {File} could not be read", file);
				}
			}
		}
		catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
		{
			_logger.LogWarning(ex, "No reference players: the index could not be read");
		}

		return players;
	}
}
