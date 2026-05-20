using System.Runtime.InteropServices;
using System.Text;

namespace ProcessGovernor.Core;

internal static class NativeMethods
{
    // Used for low-overhead total system memory metrics without WMI.
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    // Used for low-overhead total CPU usage calculation without WMI.
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    // Used for low-overhead per-process disk I/O counters. Access can fail for protected processes.
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetProcessIoCounters(IntPtr processHandle, out IoCounters ioCounters);

    // Windows exposes process suspend/resume through ntdll. These are real native calls, but access may be denied.
    [DllImport("ntdll.dll")]
    internal static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    internal static extern int NtResumeProcess(IntPtr processHandle);

    // Enables a dark native title bar on supported Windows 10/11 builds.
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    // Foreground window reads power Phase 3 window/fullscreen triggers without WMI or extra polling loops.
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    // PDH is the least invasive built-in path for summary GPU activity on Windows.
    // It can be absent or disabled on some systems, so callers must keep an Unavailable fallback.
    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    internal static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    internal static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW", CharSet = CharSet.Unicode)]
    internal static extern uint PdhGetFormattedCounterArray(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCloseQuery(IntPtr query);

    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint ProcessSetInformation = 0x0200;
    internal const uint ProcessSuspendResume = 0x0800;
    internal const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    internal const int DwmWindowAttributeUseImmersiveDarkModeBefore20H1 = 19;
    internal const uint MonitorDefaultToNearest = 0x00000002;
    internal const uint ErrorSuccess = 0x00000000;
    internal const uint PdhCstatusNewData = 0x00000001;
    internal const uint PdhMoreData = 0x800007D2;
    internal const uint PdhFmtDouble = 0x00000200;

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;

        public static MemoryStatusEx Create()
        {
            return new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;

        public ulong TotalTransferBytes => ReadTransferCount + WriteTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;

        public static MonitorInfo Create()
        {
            return new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PdhFmtCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PdhFmtCounterValueItem
    {
        public IntPtr Name;
        public PdhFmtCounterValue FmtValue;
    }
}
