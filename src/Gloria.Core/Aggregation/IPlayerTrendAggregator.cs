using Gloria.Core.Metrics;

namespace Gloria.Core.Aggregation;

/// <summary>A summary of encounter metrics over some scope (round, match, or period).</summary>
public sealed record MetricSummary(
    int EncounterCount,
    double? MedianTimeToFirstShot,
    double? MedianTimeToOnTarget,
    double MovingShotRatio);

public interface IPlayerTrendAggregator
{
    MetricSummary Summarize(IEnumerable<EncounterMetrics> metrics);
}
