using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using ProcessGovernor.Core;
using ProcessGovernor.Models;
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
        ApplyDensity(_settingsService.Current.CompactMode);
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    public void AllowExit() => _allowExit = true;

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Dispatcher.InvokeAsync(() => ApplyDensity(settings.CompactMode));
    }

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
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }

    private void ApplyDensity(bool compactMode)
    {
        RootShell.SetValue(TextElement.FontSizeProperty, compactMode ? 12.0 : 13.0);
        Sidebar.Padding = compactMode ? new Thickness(12) : new Thickness(16);
        PageHost.Margin = compactMode ? new Thickness(14) : new Thickness(22);
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
