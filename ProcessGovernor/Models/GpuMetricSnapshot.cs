namespace ProcessGovernor.Models;

public sealed class GpuMetricSnapshot
{
    public double? UsagePercent { get; init; }

    public string Display { get; init; } = "Unavailable";

    public string Source { get; init; } = "Unavailable";

    public string? Detail { get; init; }

    public bool IsAvailable => UsagePercent is not null;

    public static GpuMetricSnapshot Available(double usagePercent, string source)
        => new()
        {
            UsagePercent = Math.Clamp(usagePercent, 0, 100),
            Display = $"{Math.Clamp(usagePercent, 0, 100):0}%",
            Source = source
        };

    public static GpuMetricSnapshot Unavailable(string? detail = null)
        => new()
        {
            Detail = detail
        };
}
