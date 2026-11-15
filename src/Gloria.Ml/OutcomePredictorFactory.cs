namespace Gloria.Ml;

/// <summary>
/// Picks the outcome model the app uses: the one in the user's model folder, then the base one shipped with
/// the app. Unlike the aim classifier there is no rule-based fallback, so this can return null.
/// </summary>
public static class OutcomePredictorFactory
{
    /// <param name="userModelDirectory">The aim model folder; the outcome bundle is its <see cref="OutcomeModelStore.SubfolderName"/>.</param>
    /// <param name="onRejected">Told why a model folder that exists could not be used.</param>
    public static MlOutcomePredictor? Create(string userModelDirectory, string baseModelDirectory, Action<string>? onRejected = null)
    {
        var user = UserModelFolder.ActiveDirectory(userModelDirectory) ?? userModelDirectory;
        foreach (var directory in new[] { user, baseModelDirectory })
        {
            var outcomes = Path.Combine(directory, OutcomeModelStore.SubfolderName);
            if (!OutcomeModelStore.Exists(outcomes))
                continue;

            try
            {
                return new MlOutcomePredictor(OutcomeModelStore.Load(outcomes));
            }
            catch (AimModelException ex)
            {
                onRejected?.Invoke(ex.Message);
            }
        }

        return null;
    }
}
