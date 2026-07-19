namespace ProcessGovernor.Models;

public sealed class SystemSummary
{
    public double CpuUsagePercent { get; init; }

    public double MemoryUsagePercent { get; init; }

    public long UsedMemoryBytes { get; init; }

    public long TotalMemoryBytes { get; init; }

    public string DiskActivity { get; init; } = "Unavailable";

    public double? DiskBytesPerSecond { get; init; }

    public double? DiskReadBytesPerSecond { get; init; }

    public double? DiskWriteBytesPerSecond { get; init; }

    public string GpuUsage { get; init; } = "Unavailable";

    public double? GpuUsagePercent { get; init; }

    public string GpuEngine { get; init; } = "Unavailable";

    public string GpuDetail { get; init; } = "Unavailable";

    public int ProcessCount { get; init; }

    public TimeSpan Uptime { get; init; }

    public string CpuDisplay => $"{CpuUsagePercent:0}%";

    public string MemoryDisplay => $"{MemoryUsagePercent:0}%";

    public string MemoryDetail => $"{ProcessSnapshot.FormatBytes(UsedMemoryBytes)} / {ProcessSnapshot.FormatBytes(TotalMemoryBytes)}";

    public string DiskDetail => DiskReadBytesPerSecond is null && DiskWriteBytesPerSecond is null
        ? "Read/write unavailable"
        : $"R {FormatRate(DiskReadBytesPerSecond)} | W {FormatRate(DiskWriteBytesPerSecond)}";

    public string UptimeDisplay
    {
        get
        {
            if (Uptime.TotalDays >= 1)
            {
                return $"{(int)Uptime.TotalDays}d {Uptime.Hours}h";
            }

            return $"{Uptime.Hours}h {Uptime.Minutes}m";
        }
    }

    private static string FormatRate(double? bytesPerSecond) => bytesPerSecond is null
        ? "Unavailable"
        : $"{ProcessSnapshot.FormatBytes((long)Math.Max(0, bytesPerSecond.Value))}/s";
}
