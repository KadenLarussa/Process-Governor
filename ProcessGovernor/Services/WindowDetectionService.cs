using System.Diagnostics;
using System.Text;
using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class WindowDetectionService : IWindowDetectionService
{
    private const int FullscreenTolerancePixels = 2;

    public WindowSnapshot? CaptureForegroundWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || processId > int.MaxValue)
        {
            return null;
        }

        var title = ReadWindowTitle(hwnd);
        var processName = ReadProcessName((int)processId);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return new WindowSnapshot
        {
            ProcessId = (int)processId,
            ProcessName = processName,
            Title = title,
            IsFullscreen = IsFullscreen(hwnd),
            WindowHandle = hwnd.ToInt64()
        };
    }

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        return NativeMethods.GetWindowText(hwnd, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    private static string ReadProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var name = process.ProcessName;
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.exe";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private static bool IsFullscreen(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = NativeMethods.MonitorInfo.Create();
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        return Covers(windowRect.Left, monitorInfo.Monitor.Left)
            && Covers(windowRect.Top, monitorInfo.Monitor.Top)
            && Covers(monitorInfo.Monitor.Right, windowRect.Right)
            && Covers(monitorInfo.Monitor.Bottom, windowRect.Bottom);
    }

    private static bool Covers(int expectedEdge, int actualEdge)
        => Math.Abs(expectedEdge - actualEdge) <= FullscreenTolerancePixels;
}
