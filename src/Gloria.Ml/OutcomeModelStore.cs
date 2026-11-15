using System.Text.Json;
using System.Text.Json.Serialization;
using Gloria.Core.Classification;
using Microsoft.ML;

namespace Gloria.Ml;

/// <summary>
/// Saves an outcome bundle as a folder: manifest.json plus one ML.NET zip per target. The base bundle
/// lives in the <c>outcomes</c> subfolder of the aim base bundle.
/// </summary>
public static class OutcomeModelStore
{
    public const string ManifestFileName = "manifest.json";

    /// <summary>Subfolder of a model folder that holds the outcome bundle.</summary>
    public const string SubfolderName = "outcomes";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool Exists(string directory) => File.Exists(Path.Combine(directory, ManifestFileName));

    public static void Save(OutcomeModelBundle bundle, string directory)
    {
        Directory.CreateDirectory(directory);
        var ml = new MLContext();
        foreach (var (target, model) in bundle.Models)
            ml.Model.Save(model, bundle.InputSchema, ModelPath(directory, target));

        // Written last, so a folder with a manifest always has its models.
        File.WriteAllText(Path.Combine(directory, ManifestFileName), JsonSerializer.Serialize(bundle.Manifest, JsonOptions));
    }

    /// <exception cref="AimModelException">The folder is unreadable or was made for another feature schema.</exception>
    public static OutcomeModelBundle Load(string directory)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);
        OutcomeModelManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<OutcomeModelManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new AimModelException($"Empty model manifest {manifestPath}");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new AimModelException($"Cannot read model manifest {manifestPath}", ex);
        }

        if (manifest.FormatVersion != OutcomeModelManifest.CurrentFormatVersion)
            throw new AimModelException($"Outcome model format {manifest.FormatVersion} is not supported (expected {OutcomeModelManifest.CurrentFormatVersion}).");
        if (manifest.FeatureSchemaVersion != EncounterFeatures.SchemaVersion)
            throw new AimModelException($"Outcome model was trained on feature schema {manifest.FeatureSchemaVersion}, but the app uses {EncounterFeatures.SchemaVersion}.");

        var ml = new MLContext();
        var models = new Dictionary<OutcomeTarget, ITransformer>();
        DataViewSchema? inputSchema = null;
        foreach (var info in manifest.Targets)
        {
            try
            {
                models[info.Target] = ml.Model.Load(ModelPath(directory, info.Target), out var schema);
                inputSchema ??= schema;
            }
            catch (Exception ex)
            {
                throw new AimModelException($"Cannot load the {info.Target} model from {directory}", ex);
            }
        }

        inputSchema ??= ml.Data.LoadFromEnumerable(Array.Empty<MlEncounterRow>()).Schema;
        return new OutcomeModelBundle(manifest, models, inputSchema);
    }

    private static string ModelPath(string directory, OutcomeTarget target) => Path.Combine(directory, $"{target}.zip");
}
