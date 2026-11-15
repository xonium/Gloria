using Gloria.Core.Classification;

namespace Gloria.Ml;

/// <summary>
/// The app's classifier, which can be replaced while the app runs, e.g. by a retrained user model.
/// Each call uses whichever classifier was current when it started.
/// </summary>
public sealed class SwitchableAimClassifier(IAimClassifier initial) : IAimClassifier
{
    private volatile IAimClassifier _current = initial;

    /// <summary>Raised after <see cref="Swap"/>, on the swapping thread.</summary>
    public event EventHandler? Changed;

    public IAimClassifier Current => _current;

    public string Source => _current.Source;

    public IReadOnlyList<AimClassification> Classify(IReadOnlyList<EncounterFeatures> encounters) => _current.Classify(encounters);

    public void Swap(IAimClassifier classifier)
    {
        _current = classifier;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
