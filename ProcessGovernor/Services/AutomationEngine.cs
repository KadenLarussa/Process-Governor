using System.Diagnostics;
using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class AutomationEngine : IAutomationEngine, IDisposable
{
    private readonly IProcessMonitorService _processMonitorService;
    private readonly IRulePersistenceService _rulePersistenceService;
    private readonly IRuleEvaluationService _ruleEvaluationService;
    private readonly IProcessActionService _processActionService;
    private readonly INotificationService _notificationService;
    private readonly ILoggingService _loggingService;
    private readonly SemaphoreSlim _evaluationLock = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _lastRuleRunUtc = new();
    private readonly Dictionary<(string RuleId, int TriggerProcessId), ActiveAutomation> _activeAutomations = new();
    private CancellationTokenSource? _engineCancellation;
    private AutomationStoreFile _store = new();

    public AutomationEngine(
        IProcessMonitorService processMonitorService,
        IRulePersistenceService rulePersistenceService,
        IRuleEvaluationService ruleEvaluationService,
        IProcessActionService processActionService,
        INotificationService notificationService,
        ILoggingService loggingService)
    {
        _processMonitorService = processMonitorService;
        _rulePersistenceService = rulePersistenceService;
        _ruleEvaluationService = ruleEvaluationService;
        _processActionService = processActionService;
        _notificationService = notificationService;
        _loggingService = loggingService;
    }

    public bool IsRunning { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _store = await _rulePersistenceService.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _engineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processMonitorService.SnapshotUpdated += OnSnapshotUpdated;
        IsRunning = true;
        return _loggingService.LogAsync(LogSeverity.Information, nameof(AutomationEngine), "Automation engine started.", cancellationToken: cancellationToken);
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        _processMonitorService.SnapshotUpdated -= OnSnapshotUpdated;
        if (_engineCancellation is not null)
        {
            await _engineCancellation.CancelAsync().ConfigureAwait(false);
            _engineCancellation.Dispose();
            _engineCancellation = null;
        }

        IsRunning = false;
        await _loggingService.LogAsync(LogSeverity.Information, nameof(AutomationEngine), "Automation engine stopped.").ConfigureAwait(false);
    }

    public async Task ReloadRulesAsync(CancellationToken cancellationToken)
    {
        _store = await _rulePersistenceService.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _loggingService.LogAsync(LogSeverity.Information, nameof(AutomationEngine), "Automation rules reloaded.", cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _processMonitorService.SnapshotUpdated -= OnSnapshotUpdated;
        _engineCancellation?.Cancel();
        _engineCancellation?.Dispose();
        _evaluationLock.Dispose();
    }

    private void OnSnapshotUpdated(object? sender, ProcessSnapshotBatch batch)
    {
        var token = _engineCancellation?.Token ?? CancellationToken.None;
        _ = Task.Run(() => EvaluateSnapshotAsync(batch, token), token);
    }

    private async Task EvaluateSnapshotAsync(ProcessSnapshotBatch batch, CancellationToken cancellationToken)
    {
        if (!await _evaluationLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await RevertCompletedAutomationsAsync(batch, cancellationToken).ConfigureAwait(false);
            await EvaluateStartedProcessesAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await _loggingService.LogAsync(LogSeverity.Error, nameof(AutomationEngine), "Automation evaluation failed.", ex.Message, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _evaluationLock.Release();
        }
    }

    private async Task EvaluateStartedProcessesAsync(ProcessSnapshotBatch batch, CancellationToken cancellationToken)
    {
        if (batch.StartedProcessIds.Count == 0)
        {
            return;
        }

        var startedProcesses = batch.Processes.Where(process => batch.StartedProcessIds.Contains(process.ProcessId)).ToList();
        foreach (var rule in _store.Rules.Where(static rule => rule.Enabled))
        {
            foreach (var process in startedProcesses)
            {
                if (!_ruleEvaluationService.IsMatch(rule, process, AutomationTriggerType.ProcessStarted) || IsOnCooldown(rule))
                {
                    continue;
                }

                _lastRuleRunUtc[rule.Id] = DateTimeOffset.UtcNow;
                await ExecuteRuleAsync(rule, process, batch, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteRuleAsync(AutomationRule rule, ProcessSnapshot triggerProcess, ProcessSnapshotBatch batch, CancellationToken cancellationToken)
    {
        await _loggingService.LogAsync(
            LogSeverity.Information,
            nameof(AutomationEngine),
            $"Rule '{rule.Name}' triggered by {triggerProcess.Name} ({triggerProcess.ProcessId}).",
            processId: triggerProcess.ProcessId,
            ruleId: rule.Id,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (rule.DelaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(rule.DelaySeconds), cancellationToken).ConfigureAwait(false);
        }

        var active = new ActiveAutomation(rule.Id, triggerProcess.ProcessId);

        foreach (var action in rule.Actions)
        {
            await ExecuteActionAsync(rule, action, triggerProcess, batch, active, cancellationToken).ConfigureAwait(false);
        }

        if (rule.RevertOnExit && active.OriginalPriorities.Count > 0)
        {
            _activeAutomations[(rule.Id, triggerProcess.ProcessId)] = active;
        }
    }

    private async Task ExecuteActionAsync(
        AutomationRule rule,
        AutomationAction action,
        ProcessSnapshot triggerProcess,
        ProcessSnapshotBatch batch,
        ActiveAutomation active,
        CancellationToken cancellationToken)
    {
        switch (action.Type)
        {
            case AutomationActionType.SetProcessPriority:
                await ExecutePriorityActionAsync(rule, action, triggerProcess, batch, active, cancellationToken).ConfigureAwait(false);
                break;
            case AutomationActionType.SendNotification:
                await ExecuteNotificationActionAsync(rule, action, triggerProcess, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await _loggingService.LogAsync(
                    LogSeverity.Warning,
                    nameof(AutomationEngine),
                    $"Action '{action.Type}' in rule '{rule.Name}' is not active in Phase 1 and was skipped.",
                    ruleId: rule.Id,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task ExecutePriorityActionAsync(
        AutomationRule rule,
        AutomationAction action,
        ProcessSnapshot triggerProcess,
        ProcessSnapshotBatch batch,
        ActiveAutomation active,
        CancellationToken cancellationToken)
    {
        var priority = action.Priority ?? ProcessPriorityClass.Normal;
        var targetName = string.IsNullOrWhiteSpace(action.TargetProcessName)
            ? triggerProcess.Name
            : action.TargetProcessName;

        var targets = batch.Processes
            .Where(process => RuleEvaluationService.MatchesProcessName(targetName, process.Name))
            .ToList();

        if (targets.Count == 0)
        {
            await _loggingService.LogAsync(LogSeverity.Warning, nameof(AutomationEngine), $"No process matched priority target '{targetName}'.", ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var target in targets)
        {
            if (rule.DryRun)
            {
                await _loggingService.LogAsync(LogSeverity.Information, nameof(AutomationEngine), $"Dry run: would set {target.Name} ({target.ProcessId}) priority to {priority}.", processId: target.ProcessId, ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }

            var originalPriority = await _processActionService.GetPriorityAsync(target.ProcessId, cancellationToken).ConfigureAwait(false);
            if (originalPriority is not null && !active.OriginalPriorities.ContainsKey(target.ProcessId))
            {
                active.OriginalPriorities[target.ProcessId] = originalPriority.Value;
            }

            var result = await _processActionService.SetPriorityAsync(target.ProcessId, priority, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                await _loggingService.LogAsync(LogSeverity.Error, nameof(AutomationEngine), result.Message, result.Exception?.Message, target.ProcessId, rule.Id, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteNotificationActionAsync(AutomationRule rule, AutomationAction action, ProcessSnapshot triggerProcess, CancellationToken cancellationToken)
    {
        var title = string.IsNullOrWhiteSpace(action.NotificationTitle)
            ? AppConstants.AppName
            : action.NotificationTitle;
        var message = string.IsNullOrWhiteSpace(action.NotificationMessage)
            ? $"Rule '{rule.Name}' triggered by {triggerProcess.Name}."
            : action.NotificationMessage;

        if (rule.DryRun)
        {
            await _loggingService.LogAsync(LogSeverity.Information, nameof(AutomationEngine), $"Dry run: would notify '{title}: {message}'.", processId: triggerProcess.ProcessId, ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        await _notificationService.ShowAsync(title, message, cancellationToken).ConfigureAwait(false);
        await _loggingService.LogAsync(LogSeverity.Information, nameof(AutomationEngine), $"Notification sent: {title}.", processId: triggerProcess.ProcessId, ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task RevertCompletedAutomationsAsync(ProcessSnapshotBatch batch, CancellationToken cancellationToken)
    {
        if (batch.ExitedProcessIds.Count == 0 || _activeAutomations.Count == 0)
        {
            return;
        }

        var completed = _activeAutomations
            .Where(pair => batch.ExitedProcessIds.Contains(pair.Key.TriggerProcessId))
            .Select(static pair => pair.Key)
            .ToList();

        foreach (var key in completed)
        {
            var active = _activeAutomations[key];
            foreach (var original in active.OriginalPriorities)
            {
                var result = await _processActionService.SetPriorityAsync(original.Key, original.Value, cancellationToken).ConfigureAwait(false);
                var severity = result.Succeeded ? LogSeverity.Information : LogSeverity.Warning;
                await _loggingService.LogAsync(severity, nameof(AutomationEngine), $"Rollback for rule {active.RuleId}: {result.Message}", processId: original.Key, ruleId: active.RuleId, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            _activeAutomations.Remove(key);
        }
    }

    private bool IsOnCooldown(AutomationRule rule)
    {
        if (!_lastRuleRunUtc.TryGetValue(rule.Id, out var lastRun))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - lastRun < TimeSpan.FromSeconds(Math.Max(0, rule.CooldownSeconds));
    }

    private sealed class ActiveAutomation
    {
        public ActiveAutomation(string ruleId, int triggerProcessId)
        {
            RuleId = ruleId;
            TriggerProcessId = triggerProcessId;
        }

        public string RuleId { get; }

        public int TriggerProcessId { get; }

        public Dictionary<int, ProcessPriorityClass> OriginalPriorities { get; } = new();
    }
}
