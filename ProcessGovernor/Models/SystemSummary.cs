namespace ProcessGovernor.Models;

public sealed class SystemSummary
{
    public double CpuUsagePercent { get; init; }

    public double MemoryUsagePercent { get; init; }

    public long UsedMemoryBytes { get; init; }

    public long TotalMemoryBytes { get; init; }

    public string DiskActivity { get; init; } = "Unavailable";

    public double? DiskBytesPerSecond { get; init; }

    public string GpuUsage { get; init; } = "Unavailable";

    public int ProcessCount { get; init; }

    public TimeSpan Uptime { get; init; }

    public string CpuDisplay => $"{CpuUsagePercent:0}%";

    public string MemoryDisplay => $"{MemoryUsagePercent:0}%";

    public string MemoryDetail => $"{ProcessSnapshot.FormatBytes(UsedMemoryBytes)} / {ProcessSnapshot.FormatBytes(TotalMemoryBytes)}";

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
}
