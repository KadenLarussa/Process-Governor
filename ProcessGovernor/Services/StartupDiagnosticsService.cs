using System.Diagnostics;
using ProcessGovernor.Core;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.ViewModels;

namespace ProcessGovernor.Services;

public sealed class StartupDiagnosticsService : IStartupDiagnosticsService
{
    private static readonly StartupCheckDefinition[] Checks =
    [
        new("folders", "Local data folders", "Create and verify config/log directories.", true),
        new("settings", "Settings file", "Load local settings and validate refresh cadence.", true),
        new("logs", "Logging pipeline", "Load the persistent log file and write a startup entry.", true),
        new("rules", "Automation store", "Load rules, profiles, and default presets.", true),
        new("engine", "Automation engine", "Initialize rule evaluation and rollback state.", true),
        new("snapshot", "Process snapshot", "Capture a safe one-shot process metric sample.", true),
        new("gpu", "GPU metrics", "Read Windows PDH GPU Engine counters if available.", false),
        new("windows", "Window detection", "Read the foreground window for title/fullscreen triggers.", false),
        new("ui", "View models", "Prepare Dashboard, Automations, Profiles, Logs, and Settings.", true)
    ];

    private readonly AppPaths _paths;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private readonly IRulePersistenceService _rulePersistenceService;
    private readonly IAutomationEngine _automationEngine;
    private readonly IProcessMonitorService _processMonitorService;
    private readonly IGpuMetricsService _gpuMetricsService;
    private readonly IWindowDetectionService _windowDetectionService;
    private readonly AppServiceProvider _provider;
    private bool _loggingReady;

    public StartupDiagnosticsService(
        AppPaths paths,
        ISettingsService settingsService,
        ILoggingService loggingService,
        IRulePersistenceService rulePersistenceService,
        IAutomationEngine automationEngine,
        IProcessMonitorService processMonitorService,
        IGpuMetricsService gpuMetricsService,
        IWindowDetectionService windowDetectionService,
        AppServiceProvider provider)
    {
        _paths = paths;
        _settingsService = settingsService;
        _loggingService = loggingService;
        _rulePersistenceService = rulePersistenceService;
        _automationEngine = automationEngine;
        _processMonitorService = processMonitorService;
        _gpuMetricsService = gpuMetricsService;
        _windowDetectionService = windowDetectionService;
        _provider = provider;
    }

    public async Task<StartupDiagnosticsResult> RunAsync(IProgress<StartupCheckUpdate> progress, CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var passed = 0;
        var warnings = 0;
        var failures = 0;

        for (var index = 0; index < Checks.Length; index++)
        {
            var definition = Checks[index];
            var stopwatch = Stopwatch.StartNew();
            progress.Report(CreateUpdate(definition, StartupCheckStatus.Running, "Running", TimeSpan.Zero, index));

            var outcome = await RunCheckAsync(definition.Id, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (outcome.Status == StartupCheckStatus.Passed)
            {
                passed++;
            }
            else if (outcome.Status == StartupCheckStatus.Warning)
            {
                warnings++;
            }
            else if (outcome.Status == StartupCheckStatus.Failed)
            {
                failures++;
            }

            progress.Report(CreateUpdate(definition, outcome.Status, outcome.Detail, stopwatch.Elapsed, index + 1));
            await TryLogAsync(
                outcome.Status == StartupCheckStatus.Failed ? LogSeverity.Error :
                outcome.Status == StartupCheckStatus.Warning ? LogSeverity.Warning :
                LogSeverity.Information,
                $"Startup check {outcome.Status}: {definition.Name}.",
                outcome.Detail,
                cancellationToken).ConfigureAwait(false);

            if (outcome.Status == StartupCheckStatus.Failed && definition.IsFatal)
            {
                await TryLogAsync(LogSeverity.Error, $"Startup diagnostics stopped at '{definition.Name}'.", outcome.Detail, cancellationToken).ConfigureAwait(false);
                break;
            }
        }

        totalStopwatch.Stop();
        var succeeded = failures == 0;
        var summary = succeeded
            ? $"Startup diagnostics passed with {warnings} warning(s) in {totalStopwatch.Elapsed.TotalMilliseconds:0} ms."
            : $"Startup diagnostics failed with {failures} failure(s) and {warnings} warning(s).";

        await TryLogAsync(
            succeeded ? LogSeverity.Information : LogSeverity.Error,
            summary,
            null,
            cancellationToken).ConfigureAwait(false);

        return new StartupDiagnosticsResult
        {
            Succeeded = succeeded,
            PassedCount = passed,
            WarningCount = warnings,
            FailedCount = failures,
            Duration = totalStopwatch.Elapsed,
            Summary = summary
        };
    }

    private async Task<StartupCheckOutcome> RunCheckAsync(string checkId, CancellationToken cancellationToken)
    {
        try
        {
            return checkId switch
            {
                "folders" => CheckFolders(),
                "settings" => await CheckSettingsAsync(cancellationToken).ConfigureAwait(false),
                "logs" => await CheckLogsAsync(cancellationToken).ConfigureAwait(false),
                "rules" => await CheckRulesAsync(cancellationToken).ConfigureAwait(false),
                "engine" => await CheckEngineAsync(cancellationToken).ConfigureAwait(false),
                "snapshot" => await CheckProcessSnapshotAsync(cancellationToken).ConfigureAwait(false),
                "gpu" => CheckGpuMetrics(),
                "windows" => CheckWindowDetection(),
                "ui" => await CheckViewModelsAsync(cancellationToken).ConfigureAwait(false),
                _ => StartupCheckOutcome.Warning("Unknown startup check.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TryLogAsync(LogSeverity.Error, $"Startup check '{checkId}' failed.", ex.ToString(), cancellationToken).ConfigureAwait(false);
            return StartupCheckOutcome.Failed(ex.ToString());
        }
    }

    private StartupCheckOutcome CheckFolders()
    {
        _paths.EnsureCreated();
        if (!Directory.Exists(_paths.ConfigDirectory) || !Directory.Exists(_paths.LogsDirectory))
        {
            return StartupCheckOutcome.Failed("Config or log directory could not be created.");
        }

        return StartupCheckOutcome.Passed($"Config and logs ready under {_paths.Root}.");
    }

    private async Task<StartupCheckOutcome> CheckSettingsAsync(CancellationToken cancellationToken)
    {
        await _settingsService.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var settings = _settingsService.Current;

        if (settings.RefreshIntervalMs < 500 || settings.MinimizedRefreshIntervalMs < 1000)
        {
            return StartupCheckOutcome.Warning("Settings loaded, but refresh cadence will be clamped by services.");
        }

        return StartupCheckOutcome.Passed($"Settings loaded. Refresh {settings.RefreshIntervalMs} ms.");
    }

    private async Task<StartupCheckOutcome> CheckLogsAsync(CancellationToken cancellationToken)
    {
        await _loggingService.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _loggingReady = true;
        await _loggingService.LogAsync(LogSeverity.Information, nameof(StartupDiagnosticsService), "Startup diagnostics started.", cancellationToken: cancellationToken).ConfigureAwait(false);
        return StartupCheckOutcome.Passed("Persistent logging loaded and writable.");
    }

    private async Task<StartupCheckOutcome> CheckRulesAsync(CancellationToken cancellationToken)
    {
        var store = await _rulePersistenceService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var ruleIds = store.Rules.Select(static rule => rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var brokenReferences = store.Profiles
            .SelectMany(static profile => profile.RuleIds)
            .Count(ruleId => !ruleIds.Contains(ruleId));

        await TryLogAsync(
            LogSeverity.Information,
            $"Automation store loaded with {store.Rules.Count} rule(s), {store.Profiles.Count} profile(s), and {brokenReferences} broken profile reference(s).",
            null,
            cancellationToken).ConfigureAwait(false);

        if (brokenReferences > 0)
        {
            return StartupCheckOutcome.Warning($"{brokenReferences} profile rule reference(s) point to missing rules.");
        }

        return StartupCheckOutcome.Passed($"{store.Rules.Count} rules and {store.Profiles.Count} profiles ready.");
    }

    private async Task<StartupCheckOutcome> CheckEngineAsync(CancellationToken cancellationToken)
    {
        await _automationEngine.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return StartupCheckOutcome.Passed("Automation engine initialized.");
    }

    private async Task<StartupCheckOutcome> CheckProcessSnapshotAsync(CancellationToken cancellationToken)
    {
        var batch = await _processMonitorService.CaptureOnceAsync(cancellationToken).ConfigureAwait(false);
        if (batch.Processes.Count == 0)
        {
            return StartupCheckOutcome.Failed("No processes were returned by the Windows process API.");
        }

        await TryLogAsync(
            LogSeverity.Information,
            $"Startup process snapshot captured {batch.Processes.Count} process(es), CPU={batch.Summary.CpuDisplay}, RAM={batch.Summary.MemoryDisplay}, GPU={batch.Summary.GpuUsage}.",
            null,
            cancellationToken).ConfigureAwait(false);

        return StartupCheckOutcome.Passed($"{batch.Processes.Count} processes sampled.");
    }

    private StartupCheckOutcome CheckGpuMetrics()
    {
        var gpu = _gpuMetricsService.CaptureSummary();
        return gpu.IsAvailable
            ? StartupCheckOutcome.Passed($"GPU summary available from {gpu.Source}: {gpu.Display}.")
            : StartupCheckOutcome.Warning($"GPU summary unavailable: {gpu.Detail ?? "Windows did not return usable counters."}");
    }

    private StartupCheckOutcome CheckWindowDetection()
    {
        var window = _windowDetectionService.CaptureForegroundWindow();
        return window is null
            ? StartupCheckOutcome.Warning("No foreground window could be read right now.")
            : StartupCheckOutcome.Passed($"Foreground: {window.ProcessName}, fullscreen={window.IsFullscreen}.");
    }

    private async Task<StartupCheckOutcome> CheckViewModelsAsync(CancellationToken cancellationToken)
    {
        if (System.Windows.Application.Current is null)
        {
            var headlessViewModel = _provider.GetRequiredService<MainViewModel>();
            return headlessViewModel.NavigationItems.Count == 5
                ? StartupCheckOutcome.Passed("Headless mode: all primary page models resolved.")
                : StartupCheckOutcome.Failed("Headless mode: primary page model registration is incomplete.");
        }

        MainViewModel? mainViewModel = null;
        await UiDispatch.InvokeAsync(() => mainViewModel = _provider.GetRequiredService<MainViewModel>()).ConfigureAwait(false);
        if (mainViewModel is null)
        {
            return StartupCheckOutcome.Failed("Main view model could not be resolved.");
        }

        await mainViewModel.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return StartupCheckOutcome.Passed("All primary pages initialized.");
    }

    private async Task TryLogAsync(LogSeverity severity, string message, string? details, CancellationToken cancellationToken)
    {
        if (!_loggingReady)
        {
            return;
        }

        try
        {
            await _loggingService.LogAsync(severity, nameof(StartupDiagnosticsService), message, details, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Startup logging must never prevent the app from opening once the checked path has passed.
        }
    }

    private static StartupCheckUpdate CreateUpdate(
        StartupCheckDefinition definition,
        StartupCheckStatus status,
        string detail,
        TimeSpan duration,
        int completedCount)
        => new(
            definition.Id,
            definition.Name,
            definition.Description,
            status,
            detail,
            duration,
            completedCount / (double)Checks.Length * 100);

    private sealed record StartupCheckDefinition(string Id, string Name, string Description, bool IsFatal);

    private sealed record StartupCheckOutcome(StartupCheckStatus Status, string Detail)
    {
        public static StartupCheckOutcome Passed(string detail) => new(StartupCheckStatus.Passed, detail);

        public static StartupCheckOutcome Warning(string detail) => new(StartupCheckStatus.Warning, detail);

        public static StartupCheckOutcome Failed(string detail) => new(StartupCheckStatus.Failed, detail);
    }
}
