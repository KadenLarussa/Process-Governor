using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media.Animation;
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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            ShellScale.ScaleX = 1;
            ShellScale.ScaleY = 1;
            return;
        }

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = ease
        });
        ShellScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.992, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        ShellScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.992, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private void PageHost_TargetUpdated(object sender, DataTransferEventArgs e)
    {
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation)
        {
            PageHost.Opacity = 1;
            PageTranslate.Y = 0;
            return;
        }

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        PageTranslate.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Dispatcher.InvokeAsync(() => ApplyDensity(settings.CompactMode));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowAppearance.TryEnableDarkTitleBar(this);
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
        Sidebar.Padding = compactMode ? new Thickness(13) : new Thickness(18);
        PageHost.Margin = compactMode ? new Thickness(15) : new Thickness(24);
    }

}
