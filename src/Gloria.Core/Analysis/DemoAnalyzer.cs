using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;

namespace Gloria.Core.Analysis;

/// <summary>One encounter with everything computed from it.</summary>
public sealed record AnalyzedEncounter(
    Encounter Encounter,
    EncounterMetrics Metrics,
    EncounterFeatures Features,
    OutcomeLabels Labels);

public sealed record DemoAnalysis(ParsedDemo Demo, IReadOnlyList<AnalyzedEncounter> Encounters, SegmentationResult Segmentation);

/// <summary>Runs segmentation, metrics, features and outcome labels for every player of a parsed demo.</summary>
public sealed class DemoAnalyzer(
    EncounterSegmenter? segmenter = null,
    IAimMetricsCalculator? metrics = null,
    IEncounterFeatureExtractor? features = null,
    OutcomeLabeler? labeler = null)
{
    private readonly EncounterSegmenter _segmenter = segmenter ?? new EncounterSegmenter();
    private readonly IAimMetricsCalculator _metrics = metrics ?? new AimMetricsCalculator();
    private readonly IEncounterFeatureExtractor _features = features ?? new EncounterFeatureExtractor();
    private readonly OutcomeLabeler _labeler = labeler ?? new OutcomeLabeler();

    /// <summary>Changes whenever any stage changes, so cached analysis output can be invalidated.</summary>
    public string VersionKey =>
        $"s{_segmenter.Version}-m{_metrics.Version}-f{EncounterFeatures.SchemaVersion}-l{_labeler.Version}";

    public DemoAnalysis Analyze(ParsedDemo demo)
    {
        var segmentation = _segmenter.SegmentAll(demo);
        var analyzed = new List<AnalyzedEncounter>(segmentation.Encounters.Count);
        foreach (var encounter in segmentation.Encounters)
        {
            if (_metrics.Calculate(demo, encounter) is not { } m)
                continue;

            analyzed.Add(new AnalyzedEncounter(
                encounter,
                m,
                _features.Extract(demo, encounter, m),
                _labeler.Label(demo, encounter, m)));
        }

        return new DemoAnalysis(demo, analyzed, segmentation);
    }
}
