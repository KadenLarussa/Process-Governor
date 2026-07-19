using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class ProcessMonitorService : IProcessMonitorService, IDisposable
{
    private static readonly long IoSampleIntervalTicks = Stopwatch.Frequency * 5;
    private static readonly long GpuSampleIntervalTicks = Stopwatch.Frequency * 5;

    private readonly ILoggingService _loggingService;
    private readonly IWindowDetectionService _windowDetectionService;
    private readonly IGpuMetricsService _gpuMetricsService;
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);
    private readonly Dictionary<int, ProcessMetricState> _metricStates = new();
    private readonly ConcurrentDictionary<int, ProcessMetadata> _metadataStates = new();
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
    private GpuMetricSnapshot _lastGpuMetrics = GpuMetricSnapshot.Unavailable();
    private long _lastGpuSampleTimestamp;

    public ProcessMonitorService(
        ILoggingService loggingService,
        ISettingsService settingsService,
        IWindowDetectionService windowDetectionService,
        IGpuMetricsService gpuMetricsService)
    {
        _loggingService = loggingService;
        _windowDetectionService = windowDetectionService;
        _gpuMetricsService = gpuMetricsService;
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
        return _loggingService.LogAsync(
            LogSeverity.Information,
            nameof(ProcessMonitorService),
            $"Process monitor started. Refresh={_normalInterval.TotalMilliseconds:0} ms, minimized refresh={_minimizedInterval.TotalMilliseconds:0} ms, pause minimized={_pauseWhenMinimized}.",
            cancellationToken: cancellationToken);
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
            await _loggingService.LogAsync(LogSeverity.Information, nameof(ProcessMonitorService), "Process monitor stopped.").ConfigureAwait(false);
        }
    }

    public Task<ProcessSnapshotBatch> CaptureOnceAsync(CancellationToken cancellationToken)
        => Task.Run(CaptureSnapshot, cancellationToken);

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

    public void InvalidateProcessMetadata(int processId) => _metadataStates.TryRemove(processId, out _);

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

                    if (!_loggedUnavailableMetrics && batch.Summary.GpuUsage == "Unavailable")
                    {
                        _loggedUnavailableMetrics = true;
                        await _loggingService.LogAsync(
                            LogSeverity.Warning,
                            nameof(ProcessMonitorService),
                            "GPU metrics are Unavailable because the Windows PDH GPU Engine counter did not return reliable data. Summary and per-process values remain Unavailable rather than estimated.",
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
        RefreshGpuMetrics(timestamp);

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
            _metadataStates.TryRemove(exitedPid, out _);
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
        var rawName = SafeRead(() => process.ProcessName, string.Empty);
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var name = rawName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? rawName : $"{rawName}.exe";
        var metadata = GetProcessMetadata(process, processId, name, timestamp);
        var totalProcessorTime = SafeRead(() => process.TotalProcessorTime, TimeSpan.Zero);
        _metricStates.TryGetValue(processId, out var previousMetrics);
        var cpuUsage = CalculateProcessCpu(previousMetrics, totalProcessorTime, timestamp);
        var ioCounters = previousMetrics.IoCounters;
        var ioTimestamp = previousMetrics.IoTimestamp;
        var ioRates = previousMetrics.IoRates;
        if (ioTimestamp == 0 || timestamp - ioTimestamp >= IoSampleIntervalTicks)
        {
            var currentIoCounters = TryReadProcessIoCounters(processId);
            ioRates = CalculateProcessIoRates(ioCounters, ioTimestamp, currentIoCounters, timestamp);
            ioCounters = currentIoCounters;
            ioTimestamp = timestamp;
        }

        _metricStates[processId] = new ProcessMetricState(
            totalProcessorTime,
            timestamp,
            ioCounters,
            ioTimestamp,
            ioRates);
        var workingSet = SafeRead(() => process.WorkingSet64, 0L);
        var diskBytesPerSecond = SumRates(ioRates.ReadBytesPerSecond, ioRates.WriteBytesPerSecond);
        _lastGpuMetrics.Processes.TryGetValue(processId, out var gpuMetrics);

        return new ProcessSnapshot
        {
            ProcessId = processId,
            Name = name,
            CpuUsagePercent = cpuUsage,
            WorkingSetBytes = workingSet,
            DiskUsage = diskBytesPerSecond is null ? "Unavailable" : $"{ProcessSnapshot.FormatBytes((long)diskBytesPerSecond.Value)}/s",
            DiskBytesPerSecond = diskBytesPerSecond,
            DiskReadBytesPerSecond = ioRates.ReadBytesPerSecond,
            DiskWriteBytesPerSecond = ioRates.WriteBytesPerSecond,
            DiskReadOperationsPerSecond = ioRates.ReadOperationsPerSecond,
            DiskWriteOperationsPerSecond = ioRates.WriteOperationsPerSecond,
            GpuUsage = gpuMetrics?.Display ?? "Unavailable",
            GpuUsagePercent = gpuMetrics?.UsagePercent,
            GpuEngine = gpuMetrics?.DominantEngine ?? "Unavailable",
            GpuDetail = gpuMetrics?.Detail ?? (_lastGpuMetrics.IsAvailable
                ? "No GPU engine activity was reported for this process in the latest Windows PDH sample."
                : _lastGpuMetrics.Detail ?? "Unavailable"),
            Priority = metadata.Priority,
            CpuAffinityMask = metadata.CpuAffinityMask,
            Status = "Running",
            StartTime = metadata.StartTime,
            ExecutablePath = metadata.ExecutablePath,
            IsCritical = metadata.IsCritical,
            RequiresElevationForActions = metadata.RequiresElevationForActions
        };
    }

    private ProcessMetadata GetProcessMetadata(Process process, int processId, string name, long timestamp)
    {
        var refreshSeconds = 12 + processId % 8;
        var refreshTicks = Stopwatch.Frequency * refreshSeconds;
        if (_metadataStates.TryGetValue(processId, out var cached) &&
            cached.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            timestamp - cached.CapturedAt < refreshTicks)
        {
            return cached;
        }

        var priority = SafeRead<ProcessPriorityClass?>(() => process.PriorityClass, null);
        var affinity = SafeRead<long?>(() => process.ProcessorAffinity.ToInt64(), null);
        var startTime = SafeRead<DateTimeOffset?>(() => process.StartTime, null);
        var executablePath = SafeRead<string?>(() => process.MainModule?.FileName, null);
        var critical = IsCriticalProcessName(name);
        var metadata = new ProcessMetadata(
            name,
            priority,
            affinity,
            startTime,
            executablePath,
            critical,
            priority is null || executablePath is null || critical,
            timestamp);
        _metadataStates[processId] = metadata;
        return metadata;
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

    private static ProcessIoRates CalculateProcessIoRates(
        NativeMethods.IoCounters? previousCounters,
        long previousTimestamp,
        NativeMethods.IoCounters? currentCounters,
        long timestamp)
    {
        if (previousTimestamp == 0 || previousCounters is null || currentCounters is null)
        {
            return default;
        }

        var elapsedSeconds = (timestamp - previousTimestamp) / (double)Stopwatch.Frequency;
        if (elapsedSeconds <= 0)
        {
            return default;
        }

        return new ProcessIoRates(
            CalculateCounterRate(previousCounters.Value.ReadTransferCount, currentCounters.Value.ReadTransferCount, elapsedSeconds),
            CalculateCounterRate(previousCounters.Value.WriteTransferCount, currentCounters.Value.WriteTransferCount, elapsedSeconds),
            CalculateCounterRate(previousCounters.Value.ReadOperationCount, currentCounters.Value.ReadOperationCount, elapsedSeconds),
            CalculateCounterRate(previousCounters.Value.WriteOperationCount, currentCounters.Value.WriteOperationCount, elapsedSeconds));
    }

    private static double? CalculateCounterRate(ulong previous, ulong current, double elapsedSeconds) =>
        current < previous ? null : (current - previous) / elapsedSeconds;

    private static double? SumRates(double? first, double? second) =>
        first is null && second is null ? null : (first ?? 0) + (second ?? 0);

    private static NativeMethods.IoCounters? TryReadProcessIoCounters(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            return NativeMethods.GetProcessIoCounters(handle, out var counters)
                ? counters
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

        var diskReadBytesPerSecond = SumSnapshotRates(snapshots, static snapshot => snapshot.DiskReadBytesPerSecond);
        var diskWriteBytesPerSecond = SumSnapshotRates(snapshots, static snapshot => snapshot.DiskWriteBytesPerSecond);
        var diskBytesPerSecond = SumRates(diskReadBytesPerSecond, diskWriteBytesPerSecond);

        return new SystemSummary
        {
            CpuUsagePercent = CaptureCpuUsage(),
            MemoryUsagePercent = Math.Clamp(memoryUsage, 0, 100),
            UsedMemoryBytes = usedMemory,
            TotalMemoryBytes = totalMemory,
            DiskActivity = diskBytesPerSecond is null
                ? "Unavailable"
                : $"{ProcessSnapshot.FormatBytes((long)diskBytesPerSecond.Value)}/s",
            DiskBytesPerSecond = diskBytesPerSecond,
            DiskReadBytesPerSecond = diskReadBytesPerSecond,
            DiskWriteBytesPerSecond = diskWriteBytesPerSecond,
            GpuUsage = _lastGpuMetrics.Display,
            GpuUsagePercent = _lastGpuMetrics.UsagePercent,
            GpuEngine = _lastGpuMetrics.DominantEngine,
            GpuDetail = _lastGpuMetrics.Detail ?? "Unavailable",
            ProcessCount = snapshots.Count,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
        };
    }

    private void RefreshGpuMetrics(long timestamp)
    {
        if (_lastGpuSampleTimestamp != 0 && timestamp - _lastGpuSampleTimestamp < GpuSampleIntervalTicks)
        {
            return;
        }

        _lastGpuMetrics = _gpuMetricsService.CaptureSummary();
        _lastGpuSampleTimestamp = timestamp;
    }

    private static double? SumSnapshotRates(
        IReadOnlyList<ProcessSnapshot> snapshots,
        Func<ProcessSnapshot, double?> selector)
    {
        var found = false;
        var total = 0d;
        foreach (var snapshot in snapshots)
        {
            var value = selector(snapshot);
            if (value is null)
            {
                continue;
            }

            found = true;
            total += value.Value;
        }

        return found ? total : null;
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

    private readonly record struct ProcessMetricState(
        TimeSpan TotalProcessorTime,
        long Timestamp,
        NativeMethods.IoCounters? IoCounters,
        long IoTimestamp,
        ProcessIoRates IoRates);

    private readonly record struct ProcessIoRates(
        double? ReadBytesPerSecond,
        double? WriteBytesPerSecond,
        double? ReadOperationsPerSecond,
        double? WriteOperationsPerSecond);

    private readonly record struct ProcessMetadata(
        string Name,
        ProcessPriorityClass? Priority,
        long? CpuAffinityMask,
        DateTimeOffset? StartTime,
        string? ExecutablePath,
        bool IsCritical,
        bool RequiresElevationForActions,
        long CapturedAt);

    private readonly record struct CpuSample(ulong Idle, ulong Kernel, ulong User);
}
