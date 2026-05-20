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
            var highestEngineUsage = 0d;
            var validCounterCount = 0;

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

                highestEngineUsage = Math.Max(highestEngineUsage, value);
                validCounterCount++;
            }

            return validCounterCount == 0
                ? GpuMetricSnapshot.Unavailable("No valid GPU engine counters were returned.")
                : GpuMetricSnapshot.Available(highestEngineUsage, SourceName);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
