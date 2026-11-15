using System.Globalization;
using Gloria.Data;
using Gloria.Data.Corrections;
using Gloria.Ml;
using Gloria.ViewModels;
using Microsoft.Extensions.Logging;

namespace Gloria.Services;

/// <summary>
/// Retrains the user's aim model from the stored encounters and the user's corrections, in the background.
/// A new model replaces the current one only if it agrees with at least as many corrections; then every stored
/// encounter is classified again. Members are meant to be used from the UI thread; the heavy work runs on the pool.
/// </summary>
public sealed class UserModelTraining : ObservableObject
{
	/// <summary>New corrections that start a retrain on their own.</summary>
	public const int RetrainAfter = 10;

	private const string ConsideredAtKey = "corrections-trained-at";

	private readonly CorrectionStore _store;
	private readonly SwitchableAimClassifier _classifier;
	private readonly AppSettings _settings;
	private readonly string _userRoot;
	private readonly ILogger<UserModelTraining> _log;
	private bool _isTraining;
	private int _corrections;
	private int _newCorrections;
	private string _status = "";
	private bool _isUserModel;

	/// <param name="isUserModel">The classifier at startup is the user's model rather than the base model or the rules.</param>
	public UserModelTraining(
		CorrectionStore store,
		SwitchableAimClassifier classifier,
		AppSettings settings,
		string userRoot,
		bool isUserModel,
		ILogger<UserModelTraining> log)
	{
		_isUserModel = isUserModel;
		_store = store;
		_classifier = classifier;
		_settings = settings;
		_userRoot = userRoot;
		_log = log;
		_classifier.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(ClassifierLabel)));
	}

	public bool IsTraining
	{
		get => _isTraining;
		private set
		{
			if (Set(ref _isTraining, value))
				OnPropertyChanged(nameof(ClassifierLabel));
		}
	}

	/// <summary>Category verdicts the user has given, in total.</summary>
	public int Corrections { get => _corrections; private set => Set(ref _corrections, value); }

	/// <summary>Verdicts given or changed since the model was last retrained (or a retrain was tried).</summary>
	public int NewCorrections { get => _newCorrections; private set => Set(ref _newCorrections, value); }

	/// <summary>What the last retrain did, for the user.</summary>
	public string Status
	{
		get => _status;
		private set
		{
			if (Set(ref _status, value))
				OnPropertyChanged(nameof(HasStatus));
		}
	}

	public bool HasStatus => Status.Length > 0;

	public bool IsUserModel => _isUserModel;

	public string ClassifierLabel =>
		// Source reads "model <version>" or "rules".
		(IsUserModel ? $"Your {_classifier.Source}" : $"Classifier: {_classifier.Source}")
		+ (IsTraining ? " · retraining…" : "");

	private DateTimeOffset? ConsideredAt
	{
		get => DateTimeOffset.TryParse(_settings.Get(ConsideredAtKey), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;
		set => _settings.Set(ConsideredAtKey, value?.ToString("O", CultureInfo.InvariantCulture));
	}

	/// <summary>
	/// Classifies stored encounters that an older model classified, e.g. after an update shipped a new base model,
	/// then finishes a retrain that enough corrections asked for but that didn't complete (the app was closed).
	/// </summary>
	public async Task CatchUpAsync()
	{
		try
		{
			var count = await Task.Run(() => _store.ReclassifyAsync(_classifier));
			if (count > 0)
				_log.LogInformation("Reclassified {Count} encounters with {Source}", count, _classifier.Source);
			await RefreshCountsAsync();
		}
		catch (Exception ex)
		{
			_log.LogError(ex, "Reclassifying stored encounters failed");
			return;
		}

		if (NewCorrections >= RetrainAfter)
			await RetrainAsync();
	}

	public async Task RefreshCountsAsync()
	{
		Corrections = await _store.CountAsync();
		NewCorrections = await _store.CountAsync(ConsideredAt);
	}

	/// <summary>Call after the user set or cleared a verdict; retrains once enough new ones have piled up.</summary>
	public async Task OnCorrectedAsync()
	{
		await RefreshCountsAsync();
		if (NewCorrections >= RetrainAfter)
			await RetrainAsync();
	}

	public async Task RetrainAsync()
	{
		if (IsTraining)
			return;

		IsTraining = true;
		Status = "Retraining your model…";
		try
		{
			var startedAt = DateTimeOffset.UtcNow;
			var outcome = await Task.Run(TrainAsync);
			ConsideredAt = startedAt;
			Status = outcome;
		}
		catch (Exception ex)
		{
			_log.LogError(ex, "Retraining the user model failed");
			Status = $"Retraining failed: {ex.Message}";
		}
		finally
		{
			IsTraining = false;
			await RefreshCountsAsync();
		}
	}

	private async Task<string> TrainAsync()
	{
		var rows = await _store.TrainingSetAsync();
		var labels = rows
			.Where(r => r.Corrections is not null)
			.SelectMany(r => r.Corrections!.Select(c => new CorrectedLabel(r.Features, c.Key, c.Value)))
			.ToList();
		if (labels.Count == 0)
			return "Nothing to learn from yet: correct some encounters first.";

		var bundle = new AimModelTrainer().Train(rows.Select(r => new TrainingEncounter(r.Features, r.DemoHash, r.Corrections)).ToList());
		var candidate = new MlAimClassifier(bundle);
		var current = _classifier.Current;
		var before = Agreement.Of(current, labels);
		var after = Agreement.Of(candidate, labels);
		if (after.Agreed < before.Agreed)
			return $"Kept the current model: it agrees with {before} of your corrections, the retrained one only with {after}.";

		UserModelFolder.Promote(_userRoot, bundle);
		_isUserModel = true;
		_classifier.Swap(candidate);
		var reclassified = await _store.ReclassifyAsync(_classifier);
		_log.LogInformation("Promoted user model {Version}; reclassified {Count} encounters", bundle.Manifest.ModelVersion, reclassified);
		return $"Retrained on {rows.Count:N0} encounters with your {labels.Count} corrections. It agrees with {after} of them (the previous model: {before}).";
	}
}
