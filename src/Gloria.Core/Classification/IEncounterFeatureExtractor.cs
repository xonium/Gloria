using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;

namespace Gloria.Core.Classification;

/// <summary>Turns an analyzed encounter into the feature vector the classifiers read.</summary>
public interface IEncounterFeatureExtractor
{
    EncounterFeatures Extract(ParsedDemo demo, Encounter encounter, EncounterMetrics metrics);
}
