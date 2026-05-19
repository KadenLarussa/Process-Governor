using System.Diagnostics;
using System.Windows;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public interface IJsonFileStore
{
    Task<T> LoadAsync<T>(string path, Func<T> fallbackFactory, CancellationToken cancellationToken);

    Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken);
}

public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    Task InitializeAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface ILoggingService
{
    event EventHandler<LogEntry>? EntryAdded;

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LogEntry>> GetEntriesAsync(CancellationToken cancellationToken);

    Task LogAsync(LogSeverity severity, string source, string message, string? details = null, int? processId = null, string? ruleId = null, CancellationToken cancellationToken = default);

    Task ExportJsonAsync(string path, CancellationToken cancellationToken);

    Task ExportCsvAsync(string path, CancellationToken cancellationToken);
}

public interface IProcessMonitorService
{
    event EventHandler<ProcessSnapshotBatch>? SnapshotUpdated;

    ProcessSnapshotBatch? LatestSnapshot { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();

    void ApplySettings(AppSettings settings);

    void SetWindowMinimized(bool isMinimized);
}

public interface IProcessActionService
{
    bool IsCriticalProcess(ProcessSnapshot process);

    Task<ProcessActionResult> KillAsync(int processId, bool entireProcessTree, bool forceCriticalProcess, CancellationToken cancellationToken);

    Task<ProcessActionResult> SetPriorityAsync(int processId, ProcessPriorityClass priority, CancellationToken cancellationToken);

    Task<ProcessPriorityClass?> GetPriorityAsync(int processId, CancellationToken cancellationToken);

    Task<ProcessActionResult> SetCpuAffinityAsync(int processId, long affinityMask, CancellationToken cancellationToken);

    Task<long?> GetCpuAffinityAsync(int processId, CancellationToken cancellationToken);

    Task<ProcessActionResult> SuspendAsync(int processId, bool forceCriticalProcess, CancellationToken cancellationToken);

    Task<ProcessActionResult> ResumeAsync(int processId, CancellationToken cancellationToken);

    Task<ProcessActionResult> OpenFileLocationAsync(string? executablePath, CancellationToken cancellationToken);

    Task<ProcessActionResult> CopyPathAsync(string? executablePath, CancellationToken cancellationToken);
}

public interface IRulePersistenceService
{
    Task<AutomationStoreFile> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AutomationStoreFile store, CancellationToken cancellationToken);
}

public interface IRuleEvaluationService
{
    bool IsMatch(AutomationRule rule, ProcessSnapshot process, AutomationTriggerType triggerType);
}

public interface IAutomationEngine
{
    bool IsRunning { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();

    Task ReloadRulesAsync(CancellationToken cancellationToken);
}

public interface IPowerPlanService
{
    Task<string?> GetActivePlanAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PowerPlanInfo>> GetAvailablePlansAsync(CancellationToken cancellationToken);

    Task<ProcessActionResult> SetActivePlanByNameAsync(string planName, CancellationToken cancellationToken);
}

public interface INotificationService : IDisposable
{
    void Attach(Window window);

    Task ShowAsync(string title, string message, CancellationToken cancellationToken);
}

public interface IElevationService
{
    bool IsElevated { get; }
}

public interface IDialogService
{
    bool Confirm(string title, string message);

    void ShowInformation(string title, string message);

    void ShowWarning(string title, string message);

    void ShowError(string title, string message);
}
