namespace ProcessGovernor.Models;

public sealed class GpuMetricSnapshot
{
    private static readonly IReadOnlyDictionary<int, ProcessGpuMetric> NoProcesses =
        new Dictionary<int, ProcessGpuMetric>();

    public double? UsagePercent { get; init; }

    public string Display { get; init; } = "Unavailable";

    public string Source { get; init; } = "Unavailable";

    public string? Detail { get; init; }

    public string DominantEngine { get; init; } = "Unavailable";

    public IReadOnlyDictionary<int, ProcessGpuMetric> Processes { get; init; } = NoProcesses;

    public bool IsAvailable => UsagePercent is not null;

    public static GpuMetricSnapshot Available(
        double usagePercent,
        string source,
        string dominantEngine = "Unavailable",
        IReadOnlyDictionary<int, ProcessGpuMetric>? processes = null,
        string? detail = null)
        => new()
        {
            UsagePercent = Math.Clamp(usagePercent, 0, 100),
            Display = $"{Math.Clamp(usagePercent, 0, 100):0}%",
            Source = source,
            DominantEngine = dominantEngine,
            Detail = detail ?? (dominantEngine == "Unavailable" ? source : $"{dominantEngine} engine | {source}"),
            Processes = processes ?? NoProcesses
        };

    public static GpuMetricSnapshot Unavailable(string? detail = null)
        => new()
        {
            Detail = detail
        };
}

public sealed class ProcessGpuMetric
{
    public required int ProcessId { get; init; }

    public required double UsagePercent { get; init; }

    public required string DominantEngine { get; init; }

    public required IReadOnlyDictionary<string, double> Engines { get; init; }

    public string Display => $"{UsagePercent:0.0}%";

    public string Detail => Engines.Count == 0
        ? "Unavailable"
        : string.Join(
            " | ",
            Engines
                .OrderByDescending(static engine => engine.Value)
                .Select(static engine => $"{engine.Key} {engine.Value:0.0}%"));
}
