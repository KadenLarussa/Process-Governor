using System.Diagnostics;

namespace ProcessGovernor.Models;

public sealed class ProcessSnapshot
{
    public int ProcessId { get; init; }

    public string Name { get; init; } = string.Empty;

    public double CpuUsagePercent { get; init; }

    public long WorkingSetBytes { get; init; }

    public string DiskUsage { get; init; } = "Unavailable";

    public double? DiskBytesPerSecond { get; init; }

    public string GpuUsage { get; init; } = "Unavailable";

    public ProcessPriorityClass? Priority { get; init; }

    public long? CpuAffinityMask { get; init; }

    public string Status { get; init; } = "Running";

    public DateTimeOffset? StartTime { get; init; }

    public string? ExecutablePath { get; init; }

    public bool IsCritical { get; init; }

    public bool RequiresElevationForActions { get; init; }

    public string CpuDisplay => $"{CpuUsagePercent:0.0}%";

    public string MemoryDisplay => FormatBytes(WorkingSetBytes);

    public string PriorityDisplay => Priority?.ToString() ?? "Unavailable";

    public string AffinityDisplay => CpuAffinityMask is null ? "Unavailable" : $"0x{CpuAffinityMask.Value:X}";

    public string StartTimeDisplay => StartTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Unavailable";

    public string ExecutablePathDisplay => string.IsNullOrWhiteSpace(ExecutablePath) ? "Unavailable" : ExecutablePath;

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)Math.Max(0, bytes);
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
