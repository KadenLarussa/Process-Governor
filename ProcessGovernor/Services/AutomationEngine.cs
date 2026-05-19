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
    private readonly IPowerPlanService _powerPlanService;
    private readonly INotificationService _notificationService;
    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _evaluationLock = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _lastRuleRunUtc = new();
    private readonly Dictionary<(string RuleId, int TriggerProcessId), ActiveAutomation> _activeAutomations = new();
    private readonly Dictionary<int, ProcessSnapshot> _lastSnapshotsByPid = new();
    private CancellationTokenSource? _engineCancellation;
    private AutomationStoreFile _store = new();
    private string? _lastActiveProfileId;

    public AutomationEngine(
        IProcessMonitorService processMonitorService,
        IRulePersistenceService rulePersistenceService,
        IRuleEvaluationService ruleEvaluationService,
        IProcessActionService processActionService,
        IPowerPlanService powerPlanService,
        INotificationService notificationService,
        ILoggingService loggingService,
        ISettingsService settingsService)
    {
        _processMonitorService = processMonitorService;
        _rulePersistenceService = rulePersistenceService;
        _ruleEvaluationService = ruleEvaluationService;
        _processActionService = processActionService;
        _powerPlanService = powerPlanService;
        _notificationService = notificationService;
        _loggingService = loggingService;
        _settingsService = settingsService;
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
            var exitedProcesses = GetExitedProcesses(batch);
            await EvaluateExitedProcessesAsync(batch, exitedProcesses, cancellationToken).ConfigureAwait(false);
            await EvaluateStartedProcessesAsync(batch, cancellationToken).ConfigureAwait(false);
            await EvaluateThresholdRulesAsync(batch, cancellationToken).ConfigureAwait(false);
            RememberSnapshots(batch);
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
        foreach (var rule in GetCandidateRules(batch).Where(static rule => rule.Trigger.Type == AutomationTriggerType.ProcessStarted))
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

    private async Task EvaluateExitedProcessesAsync(ProcessSnapshotBatch batch, IReadOnlyList<ProcessSnapshot> exitedProcesses, CancellationToken cancellationToken)
    {
        if (exitedProcesses.Count == 0)
        {
            return;
        }

        foreach (var rule in GetCandidateRules(batch).Where(static rule => rule.Trigger.Type == AutomationTriggerType.ProcessExited))
        {
            foreach (var process in exitedProcesses)
            {
                if (!_ruleEvaluationService.IsMatch(rule, process, AutomationTriggerType.ProcessExited) || IsOnCooldown(rule))
                {
                    continue;
                }

                _lastRuleRunUtc[rule.Id] = DateTimeOffset.UtcNow;
                await ExecuteRuleAsync(rule, process, batch, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task EvaluateThresholdRulesAsync(ProcessSnapshotBatch batch, CancellationToken cancellationToken)
    {
        foreach (var rule in GetCandidateRules(batch).Where(static rule => rule.Trigger.Type is AutomationTriggerType.CpuThreshold or AutomationTriggerType.MemoryThreshold))
        {
            if (IsOnCooldown(rule))
            {
                continue;
            }

            var triggerProcess = batch.Processes.FirstOrDefault(process => _ruleEvaluationService.IsMatch(rule, process, rule.Trigger.Type));
            if (triggerProcess is null)
            {
                continue;
            }

            _lastRuleRunUtc[rule.Id] = DateTimeOffset.UtcNow;
            await ExecuteRuleAsync(rule, triggerProcess, batch, cancellationToken).ConfigureAwait(false);
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

        if (rule.RevertOnExit
            && (active.OriginalPriorities.Count > 0
                || active.OriginalAffinities.Count > 0
                || !string.IsNullOrWhiteSpace(active.OriginalPowerPlanName)))
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
            case AutomationActionType.SetCpuAffinity:
                await ExecuteAffinityActionAsync(rule, action, triggerProcess, batch, active, cancellationToken).ConfigureAwait(false);
                break;
            case AutomationActionType.ChangePowerPlan:
                await ExecutePowerPlanActionAsync(rule, action, active, cancellationToken).ConfigureAwait(false);
                break;
            case AutomationActionType.SuspendProcess:
                await ExecuteSuspendResumeActionAsync(rule, action, triggerProcess, batch, suspend: true, cancellationToken).ConfigureAwait(false);
                break;
            case AutomationActionType.ResumeProcess:
                await ExecuteSuspendResumeActionAsync(rule, action, triggerProcess, batch, suspend: false, cancellationToken).ConfigureAwait(false);
                break;
            case AutomationActionType.SendNotification:
                await ExecuteNotificationActionAsync(rule, action, triggerProcess, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await _loggingService.LogAsync(
                    LogSeverity.Warning,
                    nameof(AutomationEngine),
                    $"Action '{action.Type}' in rule '{rule.Name}' is not active in Phase 2 and was skipped.",
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

        var targets = GetActionTargets(targetName, batch);

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

    private async Task ExecuteAffinityActionAsync(
        AutomationRule rule,
        AutomationAction action,
        ProcessSnapshot triggerProcess,
        ProcessSnapshotBatch batch,
        ActiveAutomation active,
        CancellationToken cancellationToken)
    {
        var targetName = string.IsNullOrWhiteSpace(action.TargetProcessName)
            ? triggerProcess.Name
            : action.TargetProcessName;
        var affinityMask = action.CpuAffinityMask;
        if (affinityMask is null or <= 0)
        {
            await _loggingService.LogAsync(LogSeverity.Warning, nameof(AutomationEngine), $"Rule '{rule.Name}' has no valid CPU affinity mask.", ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var target in GetActionTargets(targetName, batch))
        {
            if (rule.DryRun)
            {
                await _loggingService.LogAsync(LogSeverity.Information, nameof(AutomationEngine), $"Dry run: would set {target.Name} ({target.ProcessId}) affinity to 0x{affinityMask.Value:X}.", processId: target.ProcessId, ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }

            var originalAffinity = await _processActionService.GetCpuAffinityAsync(target.ProcessId, cancellationToken).ConfigureAwait(false);
            if (originalAffinity is not null && !active.OriginalAffinities.ContainsKey(target.ProcessId))
            {
                active.OriginalAffinities[target.ProcessId] = originalAffinity.Value;
            }

            var result = await _processActionService.SetCpuAffinityAsync(target.ProcessId, affinityMask.Value, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                await _loggingService.LogAsync(LogSeverity.Error, nameof(AutomationEngine), result.Message, result.Exception?.Message, target.ProcessId, rule.Id, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecutePowerPlanActionAsync(AutomationRule rule, AutomationAction action, ActiveAutomation active, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.PowerPlanName))
        {
            await _loggingService.LogAsync(LogSeverity.Warning, nameof(AutomationEngine), $"Rule '{rule.Name}' has no power plan name.", ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        if (rule.DryRun)
        {
            await _loggingService.LogAsync(LogSeverity.Information, nameof(AutomationEngine), $"Dry run: would switch power plan to '{action.PowerPlanName}'.", ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_settingsService.Current.AutomaticRollbackProtection && string.IsNullOrWhiteSpace(active.OriginalPowerPlanName))
        {
            active.OriginalPowerPlanName = await _powerPlanService.GetActivePlanAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = await _powerPlanService.SetActivePlanByNameAsync(action.PowerPlanName, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            await _loggingService.LogAsync(LogSeverity.Error, nameof(AutomationEngine), result.Message, result.Exception?.Message, ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteSuspendResumeActionAsync(
        AutomationRule rule,
        AutomationAction action,
        ProcessSnapshot triggerProcess,
        ProcessSnapshotBatch batch,
        bool suspend,
        CancellationToken cancellationToken)
    {
        var targetName = string.IsNullOrWhiteSpace(action.TargetProcessName)
            ? triggerProcess.Name
            : action.TargetProcessName;

        foreach (var target in GetActionTargets(targetName, batch))
        {
            if (rule.DryRun)
            {
                await _loggingService.LogAsync(LogSeverity.Information, nameof(AutomationEngine), $"Dry run: would {(suspend ? "suspend" : "resume")} {target.Name} ({target.ProcessId}).", processId: target.ProcessId, ruleId: rule.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }

            var result = suspend
                ? await _processActionService.SuspendAsync(target.ProcessId, forceCriticalProcess: false, cancellationToken).ConfigureAwait(false)
                : await _processActionService.ResumeAsync(target.ProcessId, cancellationToken).ConfigureAwait(false);
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

            foreach (var original in active.OriginalAffinities)
            {
                var result = await _processActionService.SetCpuAffinityAsync(original.Key, original.Value, cancellationToken).ConfigureAwait(false);
                var severity = result.Succeeded ? LogSeverity.Information : LogSeverity.Warning;
                await _loggingService.LogAsync(severity, nameof(AutomationEngine), $"Affinity rollback for rule {active.RuleId}: {result.Message}", processId: original.Key, ruleId: active.RuleId, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(active.OriginalPowerPlanName))
            {
                var result = await _powerPlanService.SetActivePlanByNameAsync(active.OriginalPowerPlanName, cancellationToken).ConfigureAwait(false);
                var severity = result.Succeeded ? LogSeverity.Information : LogSeverity.Warning;
                await _loggingService.LogAsync(severity, nameof(AutomationEngine), $"Power plan rollback for rule {active.RuleId}: {result.Message}", ruleId: active.RuleId, cancellationToken: cancellationToken).ConfigureAwait(false);
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

    private IReadOnlyList<AutomationRule> GetCandidateRules(ProcessSnapshotBatch batch)
    {
        var enabledRules = _store.Rules.Where(static rule => rule.Enabled).ToList();
        var activeProfile = ResolveActiveProfile(batch);
        if (activeProfile is null)
        {
            return enabledRules;
        }

        var ruleIds = activeProfile.RuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return enabledRules.Where(rule => ruleIds.Contains(rule.Id)).ToList();
    }

    private AutomationProfile? ResolveActiveProfile(ProcessSnapshotBatch batch)
    {
        var now = DateTimeOffset.UtcNow;
        var activeProfile = _store.Profiles
            .Where(profile => profile.Enabled && IsProfileActive(profile, batch, now))
            .OrderBy(static profile => profile.PriorityOrder)
            .ThenBy(static profile => profile.Name)
            .FirstOrDefault();

        if (_lastActiveProfileId != activeProfile?.Id)
        {
            _lastActiveProfileId = activeProfile?.Id;
            _ = _loggingService.LogAsync(
                LogSeverity.Information,
                nameof(AutomationEngine),
                activeProfile is null ? "No automation profile is active." : $"Automation profile active: {activeProfile.Name}.");
        }

        return activeProfile;
    }

    private static bool IsProfileActive(AutomationProfile profile, ProcessSnapshotBatch batch, DateTimeOffset now)
    {
        return profile.IsActive
            || profile.TemporaryOverrideUntilUtc > now
            || (!string.IsNullOrWhiteSpace(profile.AutoActivateProcessName)
                && batch.Processes.Any(process => RuleEvaluationService.MatchesProcessName(profile.AutoActivateProcessName, process.Name)));
    }

    private static IReadOnlyList<ProcessSnapshot> GetActionTargets(string? targetName, ProcessSnapshotBatch batch)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return [];
        }

        return batch.Processes
            .Where(process => RuleEvaluationService.MatchesProcessName(targetName, process.Name))
            .ToList();
    }

    private IReadOnlyList<ProcessSnapshot> GetExitedProcesses(ProcessSnapshotBatch batch)
    {
        return batch.ExitedProcessIds
            .Select(processId => _lastSnapshotsByPid.TryGetValue(processId, out var process) ? process : null)
            .OfType<ProcessSnapshot>()
            .ToList();
    }

    private void RememberSnapshots(ProcessSnapshotBatch batch)
    {
        _lastSnapshotsByPid.Clear();
        foreach (var process in batch.Processes)
        {
            _lastSnapshotsByPid[process.ProcessId] = process;
        }
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

        public Dictionary<int, long> OriginalAffinities { get; } = new();

        public string? OriginalPowerPlanName { get; set; }
    }
}
