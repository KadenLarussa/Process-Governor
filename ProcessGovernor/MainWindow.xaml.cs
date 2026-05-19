using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using ProcessGovernor.Core;
using ProcessGovernor.Services;

namespace ProcessGovernor;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly IProcessMonitorService _processMonitorService;
    private bool _allowExit;

    public MainWindow(ISettingsService settingsService, IProcessMonitorService processMonitorService)
    {
        _settingsService = settingsService;
        _processMonitorService = processMonitorService;
        InitializeComponent();
    }

    public void AllowExit() => _allowExit = true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableDarkTitleBar();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        var minimized = WindowState == WindowState.Minimized;
        _processMonitorService.SetWindowMinimized(minimized);

        if (minimized && _settingsService.Current.MinimizeToTray)
        {
            Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowExit && _settingsService.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            _processMonitorService.SetWindowMinimized(true);
            return;
        }

        base.OnClosing(e);
    }

    private void TryEnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        var size = sizeof(int);
        var result = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmWindowAttributeUseImmersiveDarkMode,
            ref enabled,
            size);

        if (result != 0)
        {
            _ = NativeMethods.DwmSetWindowAttribute(
                hwnd,
                NativeMethods.DwmWindowAttributeUseImmersiveDarkModeBefore20H1,
                ref enabled,
                size);
        }
    }
}
