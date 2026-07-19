using System.Windows;
using System.Windows.Interop;

namespace ProcessGovernor.Core;

internal static class WindowAppearance
{
    public static void TryEnableDarkTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        var size = sizeof(int);
        var result = NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DwmWindowAttributeUseImmersiveDarkMode,
            ref enabled,
            size);

        if (result != 0)
        {
            _ = NativeMethods.DwmSetWindowAttribute(
                handle,
                NativeMethods.DwmWindowAttributeUseImmersiveDarkModeBefore20H1,
                ref enabled,
                size);
        }
    }
}
