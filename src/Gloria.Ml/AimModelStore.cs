using System.Text.Json;
using System.Text.Json.Serialization;
using Gloria.Core.Classification;
using Microsoft.ML;

namespace Gloria.Ml;

public sealed class AimModelException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Saves a bundle as a folder: manifest.json plus one ML.NET zip per category.
/// User models must live outside the Velopack install folder, next to the database.
/// </summary>
public static class AimModelStore
{
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool Exists(string directory) => File.Exists(Path.Combine(directory, ManifestFileName));

    public static void Save(AimModelBundle bundle, string directory)
    {
        Directory.CreateDirectory(directory);
        var ml = new MLContext();
        foreach (var (category, model) in bundle.Models)
            ml.Model.Save(model, bundle.InputSchema, ModelPath(directory, category));

        // Written last, so a folder with a manifest always has its models.
        File.WriteAllText(Path.Combine(directory, ManifestFileName), JsonSerializer.Serialize(bundle.Manifest, JsonOptions));
    }

    /// <exception cref="AimModelException">The folder is unreadable or was made for another feature schema.</exception>
    public static AimModelBundle Load(string directory)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);
        AimModelManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AimModelManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new AimModelException($"Empty model manifest {manifestPath}");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new AimModelException($"Cannot read model manifest {manifestPath}", ex);
        }

        if (manifest.FormatVersion != AimModelManifest.CurrentFormatVersion)
            throw new AimModelException($"Model format {manifest.FormatVersion} is not supported (expected {AimModelManifest.CurrentFormatVersion}).");
        if (manifest.FeatureSchemaVersion != EncounterFeatures.SchemaVersion)
            throw new AimModelException($"Model was trained on feature schema {manifest.FeatureSchemaVersion}, but the app uses {EncounterFeatures.SchemaVersion}.");

        var ml = new MLContext();
        var models = new Dictionary<AimCategory, ITransformer>();
        DataViewSchema? inputSchema = null;
        foreach (var info in manifest.Categories)
        {
            try
            {
                models[info.Category] = ml.Model.Load(ModelPath(directory, info.Category), out var schema);
                inputSchema ??= schema;
            }
            catch (Exception ex)
            {
                throw new AimModelException($"Cannot load the {info.Category} model from {directory}", ex);
            }
        }

        inputSchema ??= ml.Data.LoadFromEnumerable(Array.Empty<MlEncounterRow>()).Schema;
        return new AimModelBundle(manifest, models, inputSchema);
    }

    private static string ModelPath(string directory, AimCategory category) => Path.Combine(directory, $"{category}.zip");
}
