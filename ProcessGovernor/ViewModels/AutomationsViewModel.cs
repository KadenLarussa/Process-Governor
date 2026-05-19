using System.Collections.ObjectModel;
using System.Diagnostics;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.Services;

namespace ProcessGovernor.ViewModels;

public sealed class AutomationsViewModel : ObservableObject
{
    private readonly IRulePersistenceService _rulePersistenceService;
    private readonly IAutomationEngine _automationEngine;
    private readonly IPowerPlanService _powerPlanService;
    private readonly IDialogService _dialogService;
    private AutomationStoreFile _store = new();
    private AutomationRuleViewModel? _selectedRule;
    private string _newRuleName = "New Process Rule";
    private string _newProcessName = string.Empty;
    private AutomationTriggerType _newTriggerType = AutomationTriggerType.ProcessStarted;
    private double _newThresholdValue = 80;
    private ProcessPriorityClass _newPriority = ProcessPriorityClass.High;
    private bool _newAffinityEnabled;
    private string _newAffinityMask = string.Empty;
    private bool _newPowerPlanEnabled;
    private string? _newPowerPlanName;
    private bool _newNotificationEnabled = true;
    private bool _newRevertOnExit = true;
    private bool _newDryRun;
    private int _newCooldownSeconds = 30;
    private int _newDelaySeconds;

    public AutomationsViewModel(
        IRulePersistenceService rulePersistenceService,
        IAutomationEngine automationEngine,
        IPowerPlanService powerPlanService,
        IDialogService dialogService)
    {
        _rulePersistenceService = rulePersistenceService;
        _automationEngine = automationEngine;
        _powerPlanService = powerPlanService;
        _dialogService = dialogService;

        AddRuleCommand = new AsyncRelayCommand(AddRuleAsync);
        DeleteRuleCommand = new AsyncRelayCommand(DeleteRuleAsync, () => SelectedRule is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ReloadCommand = new AsyncRelayCommand(LoadAsync);
    }

    public ObservableCollection<AutomationRuleViewModel> Rules { get; } = [];

    public IReadOnlyList<ProcessPriorityClass> AvailablePriorities { get; } =
    [
        ProcessPriorityClass.Idle,
        ProcessPriorityClass.BelowNormal,
        ProcessPriorityClass.Normal,
        ProcessPriorityClass.AboveNormal,
        ProcessPriorityClass.High
    ];

    public IReadOnlyList<AutomationTriggerType> AvailableTriggerTypes { get; } =
    [
        AutomationTriggerType.ProcessStarted,
        AutomationTriggerType.ProcessExited,
        AutomationTriggerType.CpuThreshold,
        AutomationTriggerType.MemoryThreshold
    ];

    public ObservableCollection<string> AvailablePowerPlans { get; } = [];

    public AsyncRelayCommand AddRuleCommand { get; }

    public AsyncRelayCommand DeleteRuleCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand ReloadCommand { get; }

    public AutomationRuleViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                DeleteRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewRuleName
    {
        get => _newRuleName;
        set => SetProperty(ref _newRuleName, value);
    }

    public string NewProcessName
    {
        get => _newProcessName;
        set => SetProperty(ref _newProcessName, value);
    }

    public AutomationTriggerType NewTriggerType
    {
        get => _newTriggerType;
        set => SetProperty(ref _newTriggerType, value);
    }

    public double NewThresholdValue
    {
        get => _newThresholdValue;
        set => SetProperty(ref _newThresholdValue, Math.Clamp(value, 0, 1_000_000));
    }

    public ProcessPriorityClass NewPriority
    {
        get => _newPriority;
        set => SetProperty(ref _newPriority, value);
    }

    public bool NewAffinityEnabled
    {
        get => _newAffinityEnabled;
        set => SetProperty(ref _newAffinityEnabled, value);
    }

    public string NewAffinityMask
    {
        get => _newAffinityMask;
        set => SetProperty(ref _newAffinityMask, value);
    }

    public bool NewPowerPlanEnabled
    {
        get => _newPowerPlanEnabled;
        set => SetProperty(ref _newPowerPlanEnabled, value);
    }

    public string? NewPowerPlanName
    {
        get => _newPowerPlanName;
        set => SetProperty(ref _newPowerPlanName, value);
    }

    public bool NewNotificationEnabled
    {
        get => _newNotificationEnabled;
        set => SetProperty(ref _newNotificationEnabled, value);
    }

    public bool NewRevertOnExit
    {
        get => _newRevertOnExit;
        set => SetProperty(ref _newRevertOnExit, value);
    }

    public bool NewDryRun
    {
        get => _newDryRun;
        set => SetProperty(ref _newDryRun, value);
    }

    public int NewCooldownSeconds
    {
        get => _newCooldownSeconds;
        set => SetProperty(ref _newCooldownSeconds, Math.Clamp(value, 0, 3600));
    }

    public int NewDelaySeconds
    {
        get => _newDelaySeconds;
        set => SetProperty(ref _newDelaySeconds, Math.Clamp(value, 0, 3600));
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _store = await _rulePersistenceService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var powerPlans = await _powerPlanService.GetAvailablePlansAsync(cancellationToken).ConfigureAwait(false);
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Rules.Clear();
            foreach (var rule in _store.Rules.OrderBy(static rule => rule.Name))
            {
                Rules.Add(new AutomationRuleViewModel(rule));
            }

            AvailablePowerPlans.Clear();
            foreach (var plan in powerPlans)
            {
                AvailablePowerPlans.Add(plan.Name);
            }

            NewPowerPlanName ??= AvailablePowerPlans.FirstOrDefault();
        });
    }

    private async Task AddRuleAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(NewProcessName)
            && NewTriggerType is AutomationTriggerType.ProcessStarted or AutomationTriggerType.ProcessExited)
        {
            _dialogService.ShowWarning("Automation Rule", "Enter the process name to watch, for example cs2.exe.");
            return;
        }

        var processName = NewProcessName.Trim();
        var actionTargetName = string.IsNullOrWhiteSpace(processName) ? null : processName;
        var rule = new AutomationRule
        {
            Name = string.IsNullOrWhiteSpace(NewRuleName) ? $"{NewTriggerType} rule" : NewRuleName.Trim(),
            Enabled = true,
            Trigger = new AutomationTrigger
            {
                Type = NewTriggerType,
                ProcessName = actionTargetName,
                Threshold = NewTriggerType is AutomationTriggerType.CpuThreshold or AutomationTriggerType.MemoryThreshold
                    ? NewThresholdValue
                    : null
            },
            CooldownSeconds = NewCooldownSeconds,
            DelaySeconds = NewDelaySeconds,
            RevertOnExit = NewRevertOnExit,
            DryRun = NewDryRun,
            Actions =
            [
                new AutomationAction
                {
                    Type = AutomationActionType.SetProcessPriority,
                    TargetProcessName = actionTargetName,
                    Priority = NewPriority
                }
            ]
        };

        if (NewAffinityEnabled)
        {
            if (!TryParseAffinityMask(NewAffinityMask, out var affinityMask))
            {
                _dialogService.ShowWarning("Automation Rule", "Enter CPU affinity as a decimal number or hex mask such as 0xFF.");
                return;
            }

            rule.Actions.Add(new AutomationAction
            {
                Type = AutomationActionType.SetCpuAffinity,
                TargetProcessName = actionTargetName,
                CpuAffinityMask = affinityMask
            });
        }

        if (NewPowerPlanEnabled)
        {
            if (string.IsNullOrWhiteSpace(NewPowerPlanName))
            {
                _dialogService.ShowWarning("Automation Rule", "Select a Windows power plan for the rule.");
                return;
            }

            rule.Actions.Add(new AutomationAction
            {
                Type = AutomationActionType.ChangePowerPlan,
                PowerPlanName = NewPowerPlanName
            });
        }

        if (NewNotificationEnabled)
        {
            rule.Actions.Add(new AutomationAction
            {
                Type = AutomationActionType.SendNotification,
                NotificationTitle = "Automation triggered",
                NotificationMessage = string.IsNullOrWhiteSpace(processName)
                    ? $"{NewTriggerType} threshold met. Priority set to {NewPriority}."
                    : $"{processName} detected. Priority set to {NewPriority}."
            });
        }

        _store.Rules.Add(rule);
        Rules.Add(new AutomationRuleViewModel(rule));
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        NewProcessName = string.Empty;
    }

    private static bool TryParseAffinityMask(string value, out long affinityMask)
    {
        affinityMask = 0;
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out affinityMask)
                && affinityMask > 0;
        }

        return long.TryParse(trimmed, out affinityMask) && affinityMask > 0;
    }

    private async Task DeleteRuleAsync(CancellationToken cancellationToken)
    {
        if (SelectedRule is null)
        {
            return;
        }

        var selected = SelectedRule;
        if (!_dialogService.Confirm("Delete Rule", $"Delete automation rule '{selected.Name}'?"))
        {
            return;
        }

        _store.Rules.RemoveAll(rule => rule.Id == selected.Id);
        foreach (var profile in _store.Profiles)
        {
            profile.RuleIds.Remove(selected.Id);
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Rules.Remove(selected));
        SelectedRule = null;
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        _store.Rules = Rules.Select(rule => rule.Model).ToList();
        await _rulePersistenceService.SaveAsync(_store, cancellationToken).ConfigureAwait(false);
        await _automationEngine.ReloadRulesAsync(cancellationToken).ConfigureAwait(false);
    }
}
