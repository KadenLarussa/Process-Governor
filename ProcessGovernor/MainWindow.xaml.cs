using System.ComponentModel;
using System.Windows;
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
}
