using Gloria.Core.Classification;

namespace Gloria.Ml;

/// <summary>
/// The single place that decides which classifier the app uses: the user's retrained model,
/// then the base model shipped with the app, then the rules.
/// </summary>
public static class AimClassifierFactory
{
    /// <param name="onRejected">Told why a model folder that exists could not be used.</param>
    public static IAimClassifier Create(string userModelDirectory, string baseModelDirectory, Action<string>? onRejected = null)
    {
        foreach (var directory in new[] { userModelDirectory, baseModelDirectory })
        {
            if (!AimModelStore.Exists(directory))
                continue;

            try
            {
                return new MlAimClassifier(AimModelStore.Load(directory));
            }
            catch (AimModelException ex)
            {
                onRejected?.Invoke(ex.Message);
            }
        }

        return new RuleBasedAimClassifier();
    }
}
