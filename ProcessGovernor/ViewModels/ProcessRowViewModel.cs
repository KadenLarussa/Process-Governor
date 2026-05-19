using System.Diagnostics;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;

namespace ProcessGovernor.ViewModels;

public sealed class ProcessRowViewModel : ObservableObject
{
    private string _name = string.Empty;
    private double _cpuUsagePercent;
    private long _workingSetBytes;
    private string _diskUsage = "Unavailable";
    private double? _diskBytesPerSecond;
    private string _gpuUsage = "Unavailable";
    private ProcessPriorityClass? _priority;
    private long? _cpuAffinityMask;
    private string _status = "Running";
    private DateTimeOffset? _startTime;
    private string? _executablePath;
    private bool _isCritical;
    private bool _requiresElevationForActions;

    public ProcessRowViewModel(ProcessSnapshot snapshot)
    {
        ProcessId = snapshot.ProcessId;
        UpdateFrom(snapshot);
    }

    public int ProcessId { get; }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set
        {
            if (SetProperty(ref _cpuUsagePercent, value))
            {
                OnPropertyChanged(nameof(CpuDisplay));
            }
        }
    }

    public long WorkingSetBytes
    {
        get => _workingSetBytes;
        private set
        {
            if (SetProperty(ref _workingSetBytes, value))
            {
                OnPropertyChanged(nameof(MemoryDisplay));
            }
        }
    }

    public string DiskUsage
    {
        get => _diskUsage;
        private set => SetProperty(ref _diskUsage, value);
    }

    public double? DiskBytesPerSecond
    {
        get => _diskBytesPerSecond;
        private set => SetProperty(ref _diskBytesPerSecond, value);
    }

    public string GpuUsage
    {
        get => _gpuUsage;
        private set => SetProperty(ref _gpuUsage, value);
    }

    public ProcessPriorityClass? Priority
    {
        get => _priority;
        private set
        {
            if (SetProperty(ref _priority, value))
            {
                OnPropertyChanged(nameof(PriorityDisplay));
            }
        }
    }

    public long? CpuAffinityMask
    {
        get => _cpuAffinityMask;
        private set
        {
            if (SetProperty(ref _cpuAffinityMask, value))
            {
                OnPropertyChanged(nameof(AffinityDisplay));
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public DateTimeOffset? StartTime
    {
        get => _startTime;
        private set
        {
            if (SetProperty(ref _startTime, value))
            {
                OnPropertyChanged(nameof(StartTimeDisplay));
            }
        }
    }

    public string? ExecutablePath
    {
        get => _executablePath;
        private set
        {
            if (SetProperty(ref _executablePath, value))
            {
                OnPropertyChanged(nameof(ExecutablePathDisplay));
            }
        }
    }

    public bool IsCritical
    {
        get => _isCritical;
        private set => SetProperty(ref _isCritical, value);
    }

    public bool RequiresElevationForActions
    {
        get => _requiresElevationForActions;
        private set => SetProperty(ref _requiresElevationForActions, value);
    }

    public string CpuDisplay => $"{CpuUsagePercent:0.0}%";

    public string MemoryDisplay => ProcessSnapshot.FormatBytes(WorkingSetBytes);

    public string PriorityDisplay => Priority?.ToString() ?? "Unavailable";

    public string AffinityDisplay => CpuAffinityMask is null ? "Unavailable" : $"0x{CpuAffinityMask.Value:X}";

    public string StartTimeDisplay => StartTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Unavailable";

    public string ExecutablePathDisplay => string.IsNullOrWhiteSpace(ExecutablePath) ? "Unavailable" : ExecutablePath;

    public void UpdateFrom(ProcessSnapshot snapshot)
    {
        Name = snapshot.Name;
        CpuUsagePercent = snapshot.CpuUsagePercent;
        WorkingSetBytes = snapshot.WorkingSetBytes;
        DiskUsage = snapshot.DiskUsage;
        DiskBytesPerSecond = snapshot.DiskBytesPerSecond;
        GpuUsage = snapshot.GpuUsage;
        Priority = snapshot.Priority;
        CpuAffinityMask = snapshot.CpuAffinityMask;
        Status = snapshot.Status;
        StartTime = snapshot.StartTime;
        ExecutablePath = snapshot.ExecutablePath;
        IsCritical = snapshot.IsCritical;
        RequiresElevationForActions = snapshot.RequiresElevationForActions;
    }
}
