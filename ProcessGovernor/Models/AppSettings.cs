using ProcessGovernor.Core;

namespace ProcessGovernor.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = AppConstants.ConfigSchemaVersion;

    public int RefreshIntervalMs { get; set; } = AppConstants.DefaultRefreshIntervalMs;

    public int MinimizedRefreshIntervalMs { get; set; } = AppConstants.DefaultMinimizedRefreshIntervalMs;

    public bool MinimizeToTray { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool CompactMode { get; set; }

    public bool PauseMonitoringWhenMinimized { get; set; }

    public bool SafeMode { get; set; } = true;

    public bool EnableNotifications { get; set; } = true;

    public bool AutomaticRollbackProtection { get; set; } = true;

    public string Theme { get; set; } = "Soft Graphite";

    public string AccentColor { get; set; } = "#70B7FF";

    public int MaxLogEntries { get; set; } = AppConstants.MaxRecentLogs;
}
