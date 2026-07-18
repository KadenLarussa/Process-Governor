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
    private double? _diskReadBytesPerSecond;
    private double? _diskWriteBytesPerSecond;
    private string _diskReadDisplay = "Unavailable";
    private string _diskWriteDisplay = "Unavailable";
    private string _diskOperationType = "Unavailable";
    private string _diskDetail = "Operation rate unavailable";
    private string _gpuUsage = "Unavailable";
    private double? _gpuUsagePercent;
    private string _gpuEngine = "Unavailable";
    private string _gpuDetail = "Unavailable";
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

    public double? DiskReadBytesPerSecond
    {
        get => _diskReadBytesPerSecond;
        private set
        {
            if (SetProperty(ref _diskReadBytesPerSecond, value))
            {
                OnPropertyChanged(nameof(HasDiskMetrics));
            }
        }
    }

    public double? DiskWriteBytesPerSecond
    {
        get => _diskWriteBytesPerSecond;
        private set
        {
            if (SetProperty(ref _diskWriteBytesPerSecond, value))
            {
                OnPropertyChanged(nameof(HasDiskMetrics));
            }
        }
    }

    public bool HasDiskMetrics => DiskReadBytesPerSecond is not null || DiskWriteBytesPerSecond is not null;

    public string DiskReadDisplay
    {
        get => _diskReadDisplay;
        private set => SetProperty(ref _diskReadDisplay, value);
    }

    public string DiskWriteDisplay
    {
        get => _diskWriteDisplay;
        private set => SetProperty(ref _diskWriteDisplay, value);
    }

    public string DiskOperationType
    {
        get => _diskOperationType;
        private set => SetProperty(ref _diskOperationType, value);
    }

    public string DiskDetail
    {
        get => _diskDetail;
        private set => SetProperty(ref _diskDetail, value);
    }

    public string GpuUsage
    {
        get => _gpuUsage;
        private set => SetProperty(ref _gpuUsage, value);
    }

    public double? GpuUsagePercent
    {
        get => _gpuUsagePercent;
        private set
        {
            if (SetProperty(ref _gpuUsagePercent, value))
            {
                OnPropertyChanged(nameof(GpuEngineDisplay));
            }
        }
    }

    public string GpuEngine
    {
        get => _gpuEngine;
        private set
        {
            if (SetProperty(ref _gpuEngine, value))
            {
                OnPropertyChanged(nameof(GpuEngineDisplay));
            }
        }
    }

    public string GpuEngineDisplay => GpuUsagePercent is null ? string.Empty : GpuEngine;

    public string GpuDetail
    {
        get => _gpuDetail;
        private set => SetProperty(ref _gpuDetail, value);
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

    public string PriorityDisplay => Priority switch
    {
        ProcessPriorityClass.BelowNormal => "Below normal",
        ProcessPriorityClass.AboveNormal => "Above normal",
        null => "Unavailable",
        _ => Priority.Value.ToString()
    };

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
        DiskReadBytesPerSecond = snapshot.DiskReadBytesPerSecond;
        DiskWriteBytesPerSecond = snapshot.DiskWriteBytesPerSecond;
        DiskReadDisplay = snapshot.DiskReadDisplay;
        DiskWriteDisplay = snapshot.DiskWriteDisplay;
        DiskOperationType = snapshot.DiskOperationType;
        DiskDetail = snapshot.DiskDetail;
        GpuUsage = snapshot.GpuUsage;
        GpuUsagePercent = snapshot.GpuUsagePercent;
        GpuEngine = snapshot.GpuEngine;
        GpuDetail = snapshot.GpuDetail;
        Priority = snapshot.Priority;
        CpuAffinityMask = snapshot.CpuAffinityMask;
        Status = snapshot.Status;
        StartTime = snapshot.StartTime;
        ExecutablePath = snapshot.ExecutablePath;
        IsCritical = snapshot.IsCritical;
        RequiresElevationForActions = snapshot.RequiresElevationForActions;
    }
}
