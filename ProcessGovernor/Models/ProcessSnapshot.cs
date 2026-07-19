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

    public double? DiskReadBytesPerSecond { get; init; }

    public double? DiskWriteBytesPerSecond { get; init; }

    public double? DiskReadOperationsPerSecond { get; init; }

    public double? DiskWriteOperationsPerSecond { get; init; }

    public string GpuUsage { get; init; } = "Unavailable";

    public double? GpuUsagePercent { get; init; }

    public string GpuEngine { get; init; } = "Unavailable";

    public string GpuDetail { get; init; } = "Unavailable";

    public ProcessPriorityClass? Priority { get; init; }

    public long? CpuAffinityMask { get; init; }

    public string Status { get; init; } = "Running";

    public DateTimeOffset? StartTime { get; init; }

    public string? ExecutablePath { get; init; }

    public bool IsCritical { get; init; }

    public bool RequiresElevationForActions { get; init; }

    public string CpuDisplay => $"{CpuUsagePercent:0.0}%";

    public string MemoryDisplay => FormatBytes(WorkingSetBytes);

    public string DiskReadDisplay => FormatRate(DiskReadBytesPerSecond);

    public string DiskWriteDisplay => FormatRate(DiskWriteBytesPerSecond);

    public string DiskOperationType
    {
        get
        {
            if (DiskReadBytesPerSecond is null && DiskWriteBytesPerSecond is null)
            {
                return "Unavailable";
            }

            var read = DiskReadBytesPerSecond ?? 0;
            var write = DiskWriteBytesPerSecond ?? 0;
            if (read + write <= 0)
            {
                return "Idle";
            }

            if (read > write * 1.5)
            {
                return "Read";
            }

            return write > read * 1.5 ? "Write" : "Mixed";
        }
    }

    public string DiskDetail => DiskReadOperationsPerSecond is null && DiskWriteOperationsPerSecond is null
        ? "Operation rate unavailable"
        : $"Read {DiskReadDisplay} ({DiskReadOperationsPerSecond ?? 0:0.0} ops/s) | Write {DiskWriteDisplay} ({DiskWriteOperationsPerSecond ?? 0:0.0} ops/s)";

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

    private static string FormatRate(double? bytesPerSecond) => bytesPerSecond is null
        ? "Unavailable"
        : $"{FormatBytes((long)Math.Max(0, bytesPerSecond.Value))}/s";
}
