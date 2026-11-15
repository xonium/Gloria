using Gloria.Core.Classification;

namespace Gloria.Ml;

/// <summary>
/// The single place that decides which classifier the app uses: the user's retrained model,
/// then the base model shipped with the app, then the rules.
/// </summary>
public static class AimClassifierFactory
{
    /// <param name="userModelDirectory">The root of the user's models; see <see cref="UserModelFolder"/>.</param>
    /// <param name="onRejected">Told why a model folder that exists could not be used.</param>
    /// <param name="onLoaded">Told which folder the model came from; not called when the rules are used.</param>
    public static IAimClassifier Create(string userModelDirectory, string baseModelDirectory, Action<string>? onRejected = null, Action<string>? onLoaded = null)
    {
        var user = UserModelFolder.ActiveDirectory(userModelDirectory) ?? userModelDirectory;
        foreach (var directory in new[] { user, baseModelDirectory })
        {
            if (!AimModelStore.Exists(directory))
                continue;

            try
            {
                var classifier = new MlAimClassifier(AimModelStore.Load(directory));
                onLoaded?.Invoke(directory);
                return classifier;
            }
            catch (AimModelException ex)
            {
                onRejected?.Invoke(ex.Message);
            }
        }

        return new RuleBasedAimClassifier();
    }
}
