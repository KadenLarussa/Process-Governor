using System.Diagnostics;
using System.Runtime.InteropServices;
using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class ProcessMonitorService : IProcessMonitorService, IDisposable
{
    private readonly ILoggingService _loggingService;
    private readonly IWindowDetectionService _windowDetectionService;
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);
    private readonly Dictionary<int, ProcessMetricState> _metricStates = new();
    private readonly HashSet<int> _lastProcessIds = [];
    private readonly object _stateLock = new();
    private CancellationTokenSource? _runnerCancellation;
    private Task? _runner;
    private TimeSpan _normalInterval = TimeSpan.FromMilliseconds(AppConstants.DefaultRefreshIntervalMs);
    private TimeSpan _minimizedInterval = TimeSpan.FromMilliseconds(AppConstants.DefaultMinimizedRefreshIntervalMs);
    private bool _pauseWhenMinimized;
    private bool _isMinimized;
    private bool _hasBaseline;
    private bool _loggedUnavailableMetrics;
    private CpuSample? _lastCpuSample;

    public ProcessMonitorService(
        ILoggingService loggingService,
        ISettingsService settingsService,
        IWindowDetectionService windowDetectionService)
    {
        _loggingService = loggingService;
        _windowDetectionService = windowDetectionService;
        ApplySettings(settingsService.Current);
        settingsService.SettingsChanged += (_, settings) => ApplySettings(settings);
    }

    public event EventHandler<ProcessSnapshotBatch>? SnapshotUpdated;

    public ProcessSnapshotBatch? LatestSnapshot { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_runner is not null)
        {
            return Task.CompletedTask;
        }

        _runnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runner = Task.Run(() => RunAsync(_runnerCancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_runnerCancellation is null || _runner is null)
        {
            return;
        }

        await _runnerCancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await _runner.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _runnerCancellation.Dispose();
            _runnerCancellation = null;
            _runner = null;
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        lock (_stateLock)
        {
            _normalInterval = TimeSpan.FromMilliseconds(Math.Clamp(settings.RefreshIntervalMs, 500, 60_000));
            _minimizedInterval = TimeSpan.FromMilliseconds(Math.Clamp(settings.MinimizedRefreshIntervalMs, 1000, 120_000));
            _pauseWhenMinimized = settings.PauseMonitoringWhenMinimized;
        }
    }

    public void SetWindowMinimized(bool isMinimized)
    {
        lock (_stateLock)
        {
            _isMinimized = isMinimized;
        }
    }

    public void Dispose()
    {
        _runnerCancellation?.Cancel();
        _runnerCancellation?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var shouldPause = false;
            var interval = _normalInterval;
            lock (_stateLock)
            {
                shouldPause = _isMinimized && _pauseWhenMinimized;
                interval = _isMinimized ? _minimizedInterval : _normalInterval;
            }

            if (!shouldPause)
            {
                try
                {
                    var batch = CaptureSnapshot();
                    LatestSnapshot = batch;
                    SnapshotUpdated?.Invoke(this, batch);

                    if (!_loggedUnavailableMetrics)
                    {
                        _loggedUnavailableMetrics = true;
                        await _loggingService.LogAsync(
                            LogSeverity.Warning,
                            nameof(ProcessMonitorService),
                            "GPU metrics remain Unavailable until a reliable low-overhead collector is added. Disk metrics use native process I/O counters when Windows grants access.",
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _loggingService.LogAsync(LogSeverity.Error, nameof(ProcessMonitorService), "Process snapshot failed.", ex.Message, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private ProcessSnapshotBatch CaptureSnapshot()
    {
        var processes = Process.GetProcesses();
        var snapshots = new List<ProcessSnapshot>(processes.Length);
        var currentPids = new HashSet<int>();
        var timestamp = Stopwatch.GetTimestamp();

        foreach (var process in processes)
        {
            using (process)
            {
                ProcessSnapshot? snapshot = null;
                try
                {
                    snapshot = CaptureProcess(process, timestamp);
                }
                catch
                {
                    // Individual process races are expected when a process exits during enumeration.
                }

                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                    currentPids.Add(snapshot.ProcessId);
                }
            }
        }

        var started = _hasBaseline ? currentPids.Except(_lastProcessIds).ToHashSet() : new HashSet<int>();
        var exited = _hasBaseline ? _lastProcessIds.Except(currentPids).ToHashSet() : new HashSet<int>();
        _lastProcessIds.Clear();
        foreach (var pid in currentPids)
        {
            _lastProcessIds.Add(pid);
        }

        _hasBaseline = true;

        foreach (var exitedPid in exited)
        {
            _metricStates.Remove(exitedPid);
        }

        var summary = CaptureSystemSummary(snapshots);
        var foregroundWindow = _windowDetectionService.CaptureForegroundWindow();
        return new ProcessSnapshotBatch(
            snapshots.OrderByDescending(static item => item.CpuUsagePercent).ThenBy(static item => item.Name).ToList(),
            summary,
            started,
            exited,
            foregroundWindow);
    }

    private ProcessSnapshot? CaptureProcess(Process process, long timestamp)
    {
        var processId = process.Id;
        var name = SafeRead(() => process.ProcessName, string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var totalProcessorTime = SafeRead(() => process.TotalProcessorTime, TimeSpan.Zero);
        _metricStates.TryGetValue(processId, out var previousMetrics);
        var cpuUsage = CalculateProcessCpu(previousMetrics, totalProcessorTime, timestamp);
        var totalIoBytes = TryReadProcessIoBytes(processId);
        var diskBytesPerSecond = CalculateDiskBytesPerSecond(previousMetrics, totalIoBytes, timestamp);
        _metricStates[processId] = new ProcessMetricState(totalProcessorTime, timestamp, totalIoBytes);
        var workingSet = SafeRead(() => process.WorkingSet64, 0L);
        var priority = SafeRead<ProcessPriorityClass?>(() => process.PriorityClass, null);
        var affinity = SafeRead<long?>(() => process.ProcessorAffinity.ToInt64(), null);
        var startTime = SafeRead<DateTimeOffset?>(() => process.StartTime, null);
        var executablePath = SafeRead<string?>(() => process.MainModule?.FileName, null);
        var hasExited = SafeRead(() => process.HasExited, false);
        var critical = IsCriticalProcessName(name);

        return new ProcessSnapshot
        {
            ProcessId = processId,
            Name = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.exe",
            CpuUsagePercent = cpuUsage,
            WorkingSetBytes = workingSet,
            DiskUsage = diskBytesPerSecond is null ? "Unavailable" : $"{ProcessSnapshot.FormatBytes((long)diskBytesPerSecond.Value)}/s",
            DiskBytesPerSecond = diskBytesPerSecond,
            Priority = priority,
            CpuAffinityMask = affinity,
            Status = hasExited ? "Exited" : "Running",
            StartTime = startTime,
            ExecutablePath = executablePath,
            IsCritical = critical,
            RequiresElevationForActions = priority is null || executablePath is null || critical
        };
    }

    private double CalculateProcessCpu(ProcessMetricState previous, TimeSpan totalProcessorTime, long timestamp)
    {
        if (previous.Timestamp == 0)
        {
            return 0;
        }

        var elapsedSeconds = (timestamp - previous.Timestamp) / (double)Stopwatch.Frequency;

        if (elapsedSeconds <= 0)
        {
            return 0;
        }

        var processorSeconds = (totalProcessorTime - previous.TotalProcessorTime).TotalSeconds;
        var usage = processorSeconds / elapsedSeconds / _processorCount * 100;
        return Math.Clamp(usage, 0, 100);
    }

    private double? CalculateDiskBytesPerSecond(ProcessMetricState previous, ulong? totalIoBytes, long timestamp)
    {
        if (previous.Timestamp == 0 || previous.TotalIoBytes is null || totalIoBytes is null || totalIoBytes < previous.TotalIoBytes)
        {
            return null;
        }

        var elapsedSeconds = (timestamp - previous.Timestamp) / (double)Stopwatch.Frequency;
        if (elapsedSeconds <= 0)
        {
            return null;
        }

        return (totalIoBytes.Value - previous.TotalIoBytes.Value) / elapsedSeconds;
    }

    private static ulong? TryReadProcessIoBytes(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            return NativeMethods.GetProcessIoCounters(handle, out var counters)
                ? counters.TotalTransferBytes
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private SystemSummary CaptureSystemSummary(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var memoryStatus = NativeMethods.MemoryStatusEx.Create();
        var memoryAvailable = NativeMethods.GlobalMemoryStatusEx(ref memoryStatus);
        var totalMemory = memoryAvailable ? (long)Math.Min(memoryStatus.TotalPhys, long.MaxValue) : 0;
        var usedMemory = memoryAvailable ? (long)Math.Min(memoryStatus.TotalPhys - memoryStatus.AvailPhys, long.MaxValue) : 0;
        var memoryUsage = totalMemory > 0 ? usedMemory / (double)totalMemory * 100 : 0;

        var diskBytesPerSecond = snapshots
            .Where(static snapshot => snapshot.DiskBytesPerSecond is not null)
            .Sum(static snapshot => snapshot.DiskBytesPerSecond!.Value);

        return new SystemSummary
        {
            CpuUsagePercent = CaptureCpuUsage(),
            MemoryUsagePercent = Math.Clamp(memoryUsage, 0, 100),
            UsedMemoryBytes = usedMemory,
            TotalMemoryBytes = totalMemory,
            DiskActivity = diskBytesPerSecond > 0 ? $"{ProcessSnapshot.FormatBytes((long)diskBytesPerSecond)}/s" : "0 B/s",
            DiskBytesPerSecond = diskBytesPerSecond,
            GpuUsage = "Unavailable",
            ProcessCount = snapshots.Count,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
        };
    }

    private double CaptureCpuUsage()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var sample = new CpuSample(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
        if (_lastCpuSample is null)
        {
            _lastCpuSample = sample;
            return 0;
        }

        var previous = _lastCpuSample.Value;
        _lastCpuSample = sample;
        var idleDelta = sample.Idle - previous.Idle;
        var kernelDelta = sample.Kernel - previous.Kernel;
        var userDelta = sample.User - previous.User;
        var totalDelta = kernelDelta + userDelta;

        if (totalDelta == 0)
        {
            return 0;
        }

        var busy = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
        return Math.Clamp(busy / (double)totalDelta * 100, 0, 100);
    }

    private static bool IsCriticalProcessName(string processName)
    {
        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        return normalized.Equals("Idle", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("System", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Registry", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("smss", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("csrss", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("wininit", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("winlogon", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("services", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("lsass", StringComparison.OrdinalIgnoreCase);
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return fallback;
        }
        catch (UnauthorizedAccessException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
        catch (COMException)
        {
            return fallback;
        }
    }

    private readonly record struct ProcessMetricState(TimeSpan TotalProcessorTime, long Timestamp, ulong? TotalIoBytes);

    private readonly record struct CpuSample(ulong Idle, ulong Kernel, ulong User);
}
