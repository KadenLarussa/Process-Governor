using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.Services;

namespace ProcessGovernor.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private int _refreshIntervalMs;
    private int _minimizedRefreshIntervalMs;
    private bool _minimizeToTray;
    private bool _closeToTray;
    private bool _startWithWindows;
    private bool _compactMode;
    private bool _pauseMonitoringWhenMinimized;
    private bool _safeMode;
    private bool _enableNotifications;
    private bool _automaticRollbackProtection;
    private string _theme = "Dark";
    private string _accentColor = "#4CC2FF";
    private int _maxLogEntries;

    public SettingsViewModel(ISettingsService settingsService, IDialogService dialogService)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        LoadFrom(settingsService.Current);
    }

    public AsyncRelayCommand SaveCommand { get; }

    public int RefreshIntervalMs
    {
        get => _refreshIntervalMs;
        set => SetProperty(ref _refreshIntervalMs, Math.Clamp(value, 500, 60_000));
    }

    public int MinimizedRefreshIntervalMs
    {
        get => _minimizedRefreshIntervalMs;
        set => SetProperty(ref _minimizedRefreshIntervalMs, Math.Clamp(value, 1000, 120_000));
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool CompactMode
    {
        get => _compactMode;
        set => SetProperty(ref _compactMode, value);
    }

    public bool PauseMonitoringWhenMinimized
    {
        get => _pauseMonitoringWhenMinimized;
        set => SetProperty(ref _pauseMonitoringWhenMinimized, value);
    }

    public bool SafeMode
    {
        get => _safeMode;
        set => SetProperty(ref _safeMode, value);
    }

    public bool EnableNotifications
    {
        get => _enableNotifications;
        set => SetProperty(ref _enableNotifications, value);
    }

    public bool AutomaticRollbackProtection
    {
        get => _automaticRollbackProtection;
        set => SetProperty(ref _automaticRollbackProtection, value);
    }

    public string Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    public string AccentColor
    {
        get => _accentColor;
        set => SetProperty(ref _accentColor, value);
    }

    public int MaxLogEntries
    {
        get => _maxLogEntries;
        set => SetProperty(ref _maxLogEntries, Math.Clamp(value, 100, 100_000));
    }

    public void LoadFrom(AppSettings settings)
    {
        RefreshIntervalMs = settings.RefreshIntervalMs;
        MinimizedRefreshIntervalMs = settings.MinimizedRefreshIntervalMs;
        MinimizeToTray = settings.MinimizeToTray;
        CloseToTray = settings.CloseToTray;
        StartWithWindows = settings.StartWithWindows;
        CompactMode = settings.CompactMode;
        PauseMonitoringWhenMinimized = settings.PauseMonitoringWhenMinimized;
        SafeMode = settings.SafeMode;
        EnableNotifications = settings.EnableNotifications;
        AutomaticRollbackProtection = settings.AutomaticRollbackProtection;
        Theme = settings.Theme;
        AccentColor = settings.AccentColor;
        MaxLogEntries = settings.MaxLogEntries;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var settings = new AppSettings
        {
            RefreshIntervalMs = RefreshIntervalMs,
            MinimizedRefreshIntervalMs = MinimizedRefreshIntervalMs,
            MinimizeToTray = MinimizeToTray,
            CloseToTray = CloseToTray,
            StartWithWindows = StartWithWindows,
            CompactMode = CompactMode,
            PauseMonitoringWhenMinimized = PauseMonitoringWhenMinimized,
            SafeMode = SafeMode,
            EnableNotifications = EnableNotifications,
            AutomaticRollbackProtection = AutomaticRollbackProtection,
            Theme = Theme,
            AccentColor = AccentColor,
            MaxLogEntries = MaxLogEntries
        };

        await _settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        System.Windows.Application.Current.Dispatcher.Invoke(() => _dialogService.ShowInformation("Settings Saved", "Settings were saved locally."));
    }
}
