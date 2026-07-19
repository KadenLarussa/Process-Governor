using System.Runtime.InteropServices;
using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class GpuMetricsService : IGpuMetricsService, IDisposable
{
    private const string GpuEngineCounterPath = @"\GPU Engine(*)\Utilization Percentage";
    private const string SourceName = "Windows PDH GPU Engine";

    private readonly object _sync = new();
    private IntPtr _query;
    private IntPtr _counter;
    private bool _initialized;
    private bool _unsupported;
    private string? _unsupportedReason;

    public GpuMetricSnapshot CaptureSummary()
    {
        try
        {
            lock (_sync)
            {
                if (_unsupported)
                {
                    return GpuMetricSnapshot.Unavailable(_unsupportedReason);
                }

                if (!_initialized)
                {
                    Initialize();
                }

                if (_unsupported)
                {
                    return GpuMetricSnapshot.Unavailable(_unsupportedReason);
                }

                var collectStatus = NativeMethods.PdhCollectQueryData(_query);
                if (collectStatus != NativeMethods.ErrorSuccess)
                {
                    return GpuMetricSnapshot.Unavailable($"PDH collection failed: 0x{collectStatus:X8}");
                }

                return ReadEngineCounters();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return GpuMetricSnapshot.Unavailable(ex.Message);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_query != IntPtr.Zero)
            {
                NativeMethods.PdhCloseQuery(_query);
                _query = IntPtr.Zero;
                _counter = IntPtr.Zero;
            }
        }
    }

    private void Initialize()
    {
        var openStatus = NativeMethods.PdhOpenQuery(null, IntPtr.Zero, out _query);
        if (openStatus != NativeMethods.ErrorSuccess)
        {
            MarkUnsupported($"PDH query unavailable: 0x{openStatus:X8}");
            return;
        }

        var addStatus = NativeMethods.PdhAddEnglishCounter(_query, GpuEngineCounterPath, IntPtr.Zero, out _counter);
        if (addStatus != NativeMethods.ErrorSuccess)
        {
            MarkUnsupported($"GPU Engine performance counter unavailable: 0x{addStatus:X8}");
            return;
        }

        NativeMethods.PdhCollectQueryData(_query);
        _initialized = true;
    }

    private GpuMetricSnapshot ReadEngineCounters()
    {
        var bufferSize = 0u;
        var itemCount = 0u;
        var status = NativeMethods.PdhGetFormattedCounterArray(
            _counter,
            NativeMethods.PdhFmtDouble,
            ref bufferSize,
            ref itemCount,
            IntPtr.Zero);

        if (status != NativeMethods.PdhMoreData || bufferSize == 0 || itemCount == 0)
        {
            return GpuMetricSnapshot.Unavailable($"GPU counter data unavailable: 0x{status:X8}");
        }

        if (bufferSize > int.MaxValue)
        {
            return GpuMetricSnapshot.Unavailable("GPU counter data was too large to read safely.");
        }

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            status = NativeMethods.PdhGetFormattedCounterArray(
                _counter,
                NativeMethods.PdhFmtDouble,
                ref bufferSize,
                ref itemCount,
                buffer);

            if (status != NativeMethods.ErrorSuccess)
            {
                return GpuMetricSnapshot.Unavailable($"GPU counter read failed: 0x{status:X8}");
            }

            var itemSize = Marshal.SizeOf<NativeMethods.PdhFmtCounterValueItem>();
            var validCounterCount = 0;
            var engineTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var processEngineTotals = new Dictionary<int, Dictionary<string, double>>();

            for (var index = 0u; index < itemCount; index++)
            {
                var itemPointer = IntPtr.Add(buffer, checked((int)index * itemSize));
                var item = Marshal.PtrToStructure<NativeMethods.PdhFmtCounterValueItem>(itemPointer);
                if (item.FmtValue.CStatus is not NativeMethods.ErrorSuccess and not NativeMethods.PdhCstatusNewData)
                {
                    continue;
                }

                var name = Marshal.PtrToStringUni(item.Name);
                if (string.IsNullOrWhiteSpace(name) || !name.Contains("engtype_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = item.FmtValue.DoubleValue;
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                {
                    continue;
                }

                var engineType = ExtractEngineType(name);
                if (engineType is null)
                {
                    continue;
                }

                engineTotals[engineType] = engineTotals.GetValueOrDefault(engineType) + value;
                if (TryExtractProcessId(name, out var processId) && processId > 0)
                {
                    if (!processEngineTotals.TryGetValue(processId, out var processEngines))
                    {
                        processEngines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        processEngineTotals[processId] = processEngines;
                    }

                    processEngines[engineType] = processEngines.GetValueOrDefault(engineType) + value;
                }

                validCounterCount++;
            }

            if (validCounterCount == 0 || engineTotals.Count == 0)
            {
                return GpuMetricSnapshot.Unavailable("No valid GPU engine counters were returned.");
            }

            var normalizedEngines = engineTotals.ToDictionary(
                static engine => engine.Key,
                static engine => Math.Clamp(engine.Value, 0, 100),
                StringComparer.OrdinalIgnoreCase);
            var dominantEngine = normalizedEngines.MaxBy(static engine => engine.Value);
            var processMetrics = new Dictionary<int, ProcessGpuMetric>(processEngineTotals.Count);
            foreach (var process in processEngineTotals)
            {
                var engines = process.Value.ToDictionary(
                    static engine => engine.Key,
                    static engine => Math.Clamp(engine.Value, 0, 100),
                    StringComparer.OrdinalIgnoreCase);
                var dominant = engines.MaxBy(static engine => engine.Value);
                processMetrics[process.Key] = new ProcessGpuMetric
                {
                    ProcessId = process.Key,
                    UsagePercent = dominant.Value,
                    DominantEngine = dominant.Key,
                    Engines = engines
                };
            }

            return GpuMetricSnapshot.Available(
                dominantEngine.Value,
                SourceName,
                dominantEngine.Key,
                processMetrics,
                $"{string.Join(" | ", normalizedEngines.OrderByDescending(static engine => engine.Value).Select(static engine => $"{engine.Key} {engine.Value:0.0}%"))} | {SourceName}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryExtractProcessId(string instanceName, out int processId)
    {
        const string marker = "pid_";
        var markerIndex = instanceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            processId = 0;
            return false;
        }

        var start = markerIndex + marker.Length;
        var end = instanceName.IndexOf('_', start);
        if (end < 0)
        {
            end = instanceName.Length;
        }

        return int.TryParse(instanceName.AsSpan(start, end - start), out processId);
    }

    private static string? ExtractEngineType(string instanceName)
    {
        const string marker = "engtype_";
        var markerIndex = instanceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var raw = instanceName[(markerIndex + marker.Length)..].Trim();
        if (raw.StartsWith("VideoDecode", StringComparison.OrdinalIgnoreCase))
        {
            return "Decode";
        }

        if (raw.StartsWith("VideoEncode", StringComparison.OrdinalIgnoreCase))
        {
            return "Encode";
        }

        if (raw.StartsWith("VideoProcessing", StringComparison.OrdinalIgnoreCase))
        {
            return "Video";
        }

        if (raw.StartsWith("3D", StringComparison.OrdinalIgnoreCase))
        {
            return "3D";
        }

        if (raw.StartsWith("Compute", StringComparison.OrdinalIgnoreCase))
        {
            return "Compute";
        }

        if (raw.StartsWith("Copy", StringComparison.OrdinalIgnoreCase))
        {
            return "Copy";
        }

        var separator = raw.IndexOf('_');
        var label = separator > 0 ? raw[..separator] : raw;
        return string.IsNullOrWhiteSpace(label) ? "Other" : label;
    }

    private void MarkUnsupported(string reason)
    {
        _unsupported = true;
        _unsupportedReason = reason;

        if (_query != IntPtr.Zero)
        {
            NativeMethods.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counter = IntPtr.Zero;
        }
    }
}
