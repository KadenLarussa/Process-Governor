using System.Collections.ObjectModel;
using System.Diagnostics;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.Services;

namespace ProcessGovernor.ViewModels;

public sealed class AutomationsViewModel : ObservableObject
{
    private const string SafePcBoostRuleName = "Safe PC Boost: busy app helper";
    private readonly IRulePersistenceService _rulePersistenceService;
    private readonly IAutomationEngine _automationEngine;
    private readonly IPowerPlanService _powerPlanService;
    private readonly IDialogService _dialogService;
    private bool _isCreatingRule;
    private bool _isSaving;
    private string _saveStatus = string.Empty;
    private AutomationRuleViewModel? _selectedRule;
    private AutomationRuleViewModel? _editingRule;
    private string _newRuleName = "New Process Rule";
    private string _newProcessName = string.Empty;
    private string _newWindowTitleContains = string.Empty;
    private AutomationTriggerType _newTriggerType = AutomationTriggerType.ProcessStarted;
    private double _newThresholdValue = 80;
    private ProcessPriorityClass _newPriority = ProcessPriorityClass.High;
    private bool _newAffinityEnabled;
    private string _newAffinityMask = string.Empty;
    private bool _newPowerPlanEnabled;
    private string? _newPowerPlanName;
    private bool _newNotificationEnabled = true;
    private bool _newRevertOnExit = true;
    private bool _newDryRun = true;
    private int _newCooldownSeconds = 30;
    private int _newDelaySeconds;
    private string _presetStatus = string.Empty;

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
        UseGameLaunchPresetCommand = new RelayCommand(UseGameLaunchPreset);
        UseFocusedPerformancePresetCommand = new RelayCommand(UseFocusedPerformancePreset);
        UseQuietBackgroundPresetCommand = new RelayCommand(UseQuietBackgroundPreset);
        UseFullscreenFocusPresetCommand = new RelayCommand(UseFullscreenFocusPreset);
        CreateRuleCommand = new RelayCommand(BeginNewRule);
        EditRuleCommand = new RelayCommand(BeginEditRule, () => SelectedRule is not null);
        BackToRulesCommand = new RelayCommand(() => IsCreatingRule = false);
        BrowseForAppCommand = new RelayCommand(() =>
        {
            if (_dialogService.PickExecutable() is { } name)
            {
                NewProcessName = name;
            }
        });
        UseAllCpusAffinityCommand = new RelayCommand(() => ApplyAffinityPreset(CreateAllCpusMask()));
        UseFirstHalfAffinityCommand = new RelayCommand(() => ApplyAffinityPreset(CreateHalfCpusMask(firstHalf: true)));
        UseSecondHalfAffinityCommand = new RelayCommand(() => ApplyAffinityPreset(CreateHalfCpusMask(firstHalf: false)));
    }

    public ObservableCollection<AutomationRuleViewModel> Rules { get; } = [];

    public IReadOnlyList<OptionItem<ProcessPriorityClass>> AvailablePriorities { get; } =
    [
        new(ProcessPriorityClass.Idle, "Very low background", "Use for apps that can wait."),
        new(ProcessPriorityClass.BelowNormal, "Lower background", "Keeps helper apps from competing."),
        new(ProcessPriorityClass.Normal, "Normal", "Windows default priority."),
        new(ProcessPriorityClass.AboveNormal, "Small performance boost", "Safer boost for busy apps."),
        new(ProcessPriorityClass.High, "High performance", "Use only for trusted games or tools.")
    ];

    public IReadOnlyList<OptionItem<AutomationTriggerType>> AvailableTriggerTypes { get; } =
    [
        new(AutomationTriggerType.ProcessStarted, "App opens", "Run the rule when a specific .exe starts."),
        new(AutomationTriggerType.ProcessExited, "App closes", "Run the rule when a specific .exe exits."),
        new(AutomationTriggerType.CpuThreshold, "CPU gets busy", "Run when an app crosses a CPU percentage."),
        new(AutomationTriggerType.MemoryThreshold, "RAM gets high", "Run when an app crosses a RAM amount."),
        new(AutomationTriggerType.WindowTitleDetected, "Window title appears", "Run when the foreground window title contains text."),
        new(AutomationTriggerType.FullscreenDetected, "Fullscreen app appears", "Run when a foreground app covers its monitor.")
    ];

    public ObservableCollection<string> AvailablePowerPlans { get; } = [];

    public AsyncRelayCommand AddRuleCommand { get; }

    public AsyncRelayCommand DeleteRuleCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand ReloadCommand { get; }

    public RelayCommand UseGameLaunchPresetCommand { get; }

    public RelayCommand UseFocusedPerformancePresetCommand { get; }

    public RelayCommand UseQuietBackgroundPresetCommand { get; }

    public RelayCommand UseFullscreenFocusPresetCommand { get; }

    public RelayCommand CreateRuleCommand { get; }

    public RelayCommand EditRuleCommand { get; }

    public string EditorTitle => _editingRule is null ? "New rule" : "Edit rule";

    public RelayCommand BackToRulesCommand { get; }

    public RelayCommand BrowseForAppCommand { get; }

    public bool IsCreatingRule
    {
        get => _isCreatingRule;
        private set => SetProperty(ref _isCreatingRule, value);
    }

    public string SaveStatus
    {
        get => _saveStatus;
        private set => SetProperty(ref _saveStatus, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetProperty(ref _isSaving, value);
    }

    public RelayCommand UseAllCpusAffinityCommand { get; }

    public RelayCommand UseFirstHalfAffinityCommand { get; }

    public RelayCommand UseSecondHalfAffinityCommand { get; }

    public AutomationRuleViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                DeleteRuleCommand.RaiseCanExecuteChanged();
                EditRuleCommand.RaiseCanExecuteChanged();
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

    public string NewWindowTitleContains
    {
        get => _newWindowTitleContains;
        set => SetProperty(ref _newWindowTitleContains, value);
    }

    public AutomationTriggerType NewTriggerType
    {
        get => _newTriggerType;
        set
        {
            if (SetProperty(ref _newTriggerType, value))
            {
                OnPropertyChanged(nameof(NewTriggerHelp));
                OnPropertyChanged(nameof(UsesThreshold));
                OnPropertyChanged(nameof(ThresholdLabel));
                OnPropertyChanged(nameof(UsesWindowTitle));
            }
        }
    }

    public double NewThresholdValue
    {
        get => _newThresholdValue;
        set => SetProperty(ref _newThresholdValue, Math.Clamp(value, 0, 1_000_000));
    }

    public ProcessPriorityClass NewPriority
    {
        get => _newPriority;
        set
        {
            if (SetProperty(ref _newPriority, value))
            {
                OnPropertyChanged(nameof(NewPriorityHelp));
            }
        }
    }

    public bool NewAffinityEnabled
    {
        get => _newAffinityEnabled;
        set
        {
            if (SetProperty(ref _newAffinityEnabled, value))
            {
                OnPropertyChanged(nameof(NewAffinityState));
            }
        }
    }

    public string NewAffinityMask
    {
        get => _newAffinityMask;
        set => SetProperty(ref _newAffinityMask, value);
    }

    public bool NewPowerPlanEnabled
    {
        get => _newPowerPlanEnabled;
        set
        {
            if (SetProperty(ref _newPowerPlanEnabled, value))
            {
                OnPropertyChanged(nameof(NewPowerPlanState));
            }
        }
    }

    public string? NewPowerPlanName
    {
        get => _newPowerPlanName;
        set => SetProperty(ref _newPowerPlanName, value);
    }

    public bool NewNotificationEnabled
    {
        get => _newNotificationEnabled;
        set
        {
            if (SetProperty(ref _newNotificationEnabled, value))
            {
                OnPropertyChanged(nameof(NewNotificationState));
            }
        }
    }

    public bool NewRevertOnExit
    {
        get => _newRevertOnExit;
        set
        {
            if (SetProperty(ref _newRevertOnExit, value))
            {
                OnPropertyChanged(nameof(NewRollbackState));
            }
        }
    }

    public bool NewDryRun
    {
        get => _newDryRun;
        set
        {
            if (SetProperty(ref _newDryRun, value))
            {
                OnPropertyChanged(nameof(NewDryRunState));
            }
        }
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

    public string PresetStatus
    {
        get => _presetStatus;
        set => SetProperty(ref _presetStatus, value);
    }

    public bool UsesThreshold => NewTriggerType is AutomationTriggerType.CpuThreshold or AutomationTriggerType.MemoryThreshold;

    public string ThresholdLabel => NewTriggerType == AutomationTriggerType.CpuThreshold ? "CPU (%)" : "Memory (MB)";

    public bool UsesWindowTitle => NewTriggerType is AutomationTriggerType.WindowTitleDetected or AutomationTriggerType.FullscreenDetected;

    public string NewPowerPlanState => NewPowerPlanEnabled ? "Enabled" : "Off";

    public string NewAffinityState => NewAffinityEnabled ? "Enabled" : "Off";

    public string NewNotificationState => NewNotificationEnabled ? "Enabled" : "Off";

    public string NewRollbackState => NewRevertOnExit ? "Enabled" : "Off";

    public string NewDryRunState => NewDryRun ? "Test mode" : "Live";

    public string NewTriggerHelp => NewTriggerType switch
    {
        AutomationTriggerType.ProcessStarted => "Best for games and apps. Type the executable name, for example cs2.exe.",
        AutomationTriggerType.ProcessExited => "Useful for cleanup rules after an app closes.",
        AutomationTriggerType.CpuThreshold => "Watches CPU load and boosts the matching app only after it gets busy.",
        AutomationTriggerType.MemoryThreshold => "Watches RAM usage. Threshold is entered in MB.",
        AutomationTriggerType.WindowTitleDetected => "Watches the foreground window title. Useful for launchers, editors, and game menus.",
        AutomationTriggerType.FullscreenDetected => "Detects a foreground app covering the monitor. Useful for borderless games and media apps.",
        _ => "Choose what should wake this rule up."
    };

    public string NewPriorityHelp => NewPriority switch
    {
        ProcessPriorityClass.Idle => "Lowest priority. Good for background helpers you do not care about.",
        ProcessPriorityClass.BelowNormal => "A gentle way to make a background app less competitive.",
        ProcessPriorityClass.Normal => "Leaves the app at Windows default priority.",
        ProcessPriorityClass.AboveNormal => "Recommended boost. Noticeable without being extreme.",
        ProcessPriorityClass.High => "Stronger boost for trusted foreground apps. Avoid using it for random background apps.",
        _ => "Pick how strongly Windows should favor the app."
    };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var store = await _rulePersistenceService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var powerPlans = await _powerPlanService.GetAvailablePlansAsync(cancellationToken).ConfigureAwait(false);
        await UiDispatch.InvokeAsync(() =>
        {
            Rules.Clear();
            foreach (var rule in store.Rules.OrderBy(static rule => rule.Name))
            {
                Rules.Add(new AutomationRuleViewModel(rule));
            }

            AvailablePowerPlans.Clear();
            foreach (var plan in powerPlans)
            {
                AvailablePowerPlans.Add(plan.Name);
            }

            NewPowerPlanName ??= GetPreferredPowerPlanName();
        });
    }

    private void BeginNewRule()
    {
        _editingRule = null;
        NewRuleName = string.Empty;
        NewProcessName = string.Empty;
        NewWindowTitleContains = string.Empty;
        NewTriggerType = AutomationTriggerType.ProcessStarted;
        NewThresholdValue = 80;
        NewPriority = ProcessPriorityClass.AboveNormal;
        NewAffinityEnabled = false;
        NewAffinityMask = string.Empty;
        NewPowerPlanEnabled = false;
        NewPowerPlanName = GetPreferredPowerPlanName();
        NewNotificationEnabled = true;
        NewRevertOnExit = true;
        NewDryRun = true;
        NewCooldownSeconds = 30;
        NewDelaySeconds = 0;
        PresetStatus = string.Empty;
        OnPropertyChanged(nameof(EditorTitle));
        IsCreatingRule = true;
    }

    private void BeginEditRule()
    {
        if (SelectedRule is not { } row)
        {
            return;
        }

        var rule = row.Model;
        var actions = rule.Actions.GroupBy(action => action.Type).ToDictionary(group => group.Key, group => group.ToList());
        if (!AvailableTriggerTypes.Any(trigger => trigger.Value == rule.Trigger.Type)
            || !actions.TryGetValue(AutomationActionType.SetProcessPriority, out var priorityActions)
            || priorityActions[0].Priority is not { } priority
            || !AvailablePriorities.Any(option => option.Value == priority)
            || actions.Any(pair => pair.Value.Count != 1 || pair.Key is not (
                AutomationActionType.SetProcessPriority or AutomationActionType.SetCpuAffinity
                or AutomationActionType.ChangePowerPlan or AutomationActionType.SendNotification))
            || rule.Actions.Any(action => action.Type is AutomationActionType.SetProcessPriority or AutomationActionType.SetCpuAffinity
                && !string.Equals(action.TargetProcessName ?? string.Empty, rule.Trigger.ProcessName ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogService.ShowWarning("Cannot edit this rule", "This rule uses actions or targets the editor cannot represent. Its settings were not changed.");
            return;
        }

        _editingRule = row;
        NewRuleName = rule.Name;
        NewProcessName = rule.Trigger.ProcessName ?? string.Empty;
        NewWindowTitleContains = rule.Trigger.WindowTitleContains ?? string.Empty;
        NewTriggerType = rule.Trigger.Type;
        NewThresholdValue = rule.Trigger.Threshold ?? 80;
        NewPriority = priority;
        var affinity = actions.GetValueOrDefault(AutomationActionType.SetCpuAffinity)?[0];
        NewAffinityEnabled = affinity is not null;
        NewAffinityMask = affinity is null ? string.Empty : $"0x{affinity.CpuAffinityMask:X}";
        var plan = actions.GetValueOrDefault(AutomationActionType.ChangePowerPlan)?[0];
        if (plan?.PowerPlanName is { } name && !AvailablePowerPlans.Contains(name))
        {
            AvailablePowerPlans.Add(name);
        }
        NewPowerPlanEnabled = plan is not null;
        NewPowerPlanName = plan?.PowerPlanName ?? GetPreferredPowerPlanName();
        NewNotificationEnabled = actions.ContainsKey(AutomationActionType.SendNotification);
        NewRevertOnExit = rule.RevertOnExit;
        NewDryRun = rule.DryRun;
        NewCooldownSeconds = rule.CooldownSeconds;
        NewDelaySeconds = rule.DelaySeconds;
        PresetStatus = string.Empty;
        OnPropertyChanged(nameof(EditorTitle));
        IsCreatingRule = true;
    }

    private void UseGameLaunchPreset()
    {
        NewRuleName = "Game Launch Boost";
        NewProcessName = string.Empty;
        NewWindowTitleContains = string.Empty;
        NewTriggerType = AutomationTriggerType.ProcessStarted;
        NewThresholdValue = 80;
        NewPriority = ProcessPriorityClass.High;
        NewAffinityEnabled = false;
        NewAffinityMask = string.Empty;
        NewPowerPlanEnabled = true;
        NewPowerPlanName = GetPreferredPowerPlanName();
        NewNotificationEnabled = true;
        NewRevertOnExit = true;
        NewDryRun = true;
        NewCooldownSeconds = 30;
        NewDelaySeconds = 1;
        PresetStatus = "Game Launch Boost selected";
    }

    private void UseFocusedPerformancePreset()
    {
        NewRuleName = SafePcBoostRuleName;
        NewProcessName = string.Empty;
        NewWindowTitleContains = string.Empty;
        NewTriggerType = AutomationTriggerType.CpuThreshold;
        NewThresholdValue = 55;
        NewPriority = ProcessPriorityClass.AboveNormal;
        NewAffinityEnabled = false;
        NewAffinityMask = string.Empty;
        NewPowerPlanEnabled = true;
        NewPowerPlanName = GetPreferredPowerPlanName();
        NewNotificationEnabled = true;
        NewRevertOnExit = true;
        NewDryRun = true;
        NewCooldownSeconds = 180;
        NewDelaySeconds = 0;
        PresetStatus = "Safe PC Boost selected";
    }

    private void UseQuietBackgroundPreset()
    {
        NewRuleName = "Quiet Background App";
        NewProcessName = string.Empty;
        NewWindowTitleContains = string.Empty;
        NewTriggerType = AutomationTriggerType.ProcessStarted;
        NewThresholdValue = 80;
        NewPriority = ProcessPriorityClass.BelowNormal;
        NewAffinityEnabled = false;
        NewAffinityMask = string.Empty;
        NewPowerPlanEnabled = false;
        NewNotificationEnabled = true;
        NewRevertOnExit = true;
        NewDryRun = true;
        NewCooldownSeconds = 60;
        NewDelaySeconds = 0;
        PresetStatus = "Quiet Background selected";
    }

    private void UseFullscreenFocusPreset()
    {
        NewRuleName = "Fullscreen Focus";
        NewProcessName = string.Empty;
        NewWindowTitleContains = string.Empty;
        NewTriggerType = AutomationTriggerType.FullscreenDetected;
        NewThresholdValue = 80;
        NewPriority = ProcessPriorityClass.AboveNormal;
        NewAffinityEnabled = false;
        NewAffinityMask = string.Empty;
        NewPowerPlanEnabled = true;
        NewPowerPlanName = GetPreferredPowerPlanName();
        NewNotificationEnabled = true;
        NewRevertOnExit = true;
        NewDryRun = true;
        NewCooldownSeconds = 180;
        NewDelaySeconds = 1;
        PresetStatus = "Fullscreen Focus selected";
    }

    private async Task AddRuleAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(NewProcessName)
            && NewTriggerType is AutomationTriggerType.ProcessStarted or AutomationTriggerType.ProcessExited)
        {
            _dialogService.ShowWarning("Automation Rule", "Enter the process name to watch, for example cs2.exe.");
            return;
        }

        if (NewTriggerType == AutomationTriggerType.WindowTitleDetected
            && string.IsNullOrWhiteSpace(NewWindowTitleContains))
        {
            _dialogService.ShowWarning("Automation Rule", "Enter part of the window title to watch.");
            return;
        }

        var processName = NewProcessName.Trim();
        var actionTargetName = string.IsNullOrWhiteSpace(processName) ? null : processName;
        var windowTitleText = NewWindowTitleContains.Trim();
        var rule = new AutomationRule
        {
            Name = string.IsNullOrWhiteSpace(NewRuleName) ? $"{GetTriggerLabel(NewTriggerType)} rule" : NewRuleName.Trim(),
            Enabled = true,
            Trigger = new AutomationTrigger
            {
                Type = NewTriggerType,
                ProcessName = actionTargetName,
                Threshold = NewTriggerType is AutomationTriggerType.CpuThreshold or AutomationTriggerType.MemoryThreshold
                    ? NewThresholdValue
                    : null,
                WindowTitleContains = NewTriggerType is AutomationTriggerType.WindowTitleDetected or AutomationTriggerType.FullscreenDetected
                    ? string.IsNullOrWhiteSpace(windowTitleText) ? null : windowTitleText
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
        var original = _editingRule;
        if (original is not null)
        {
            rule.Id = original.Id;
            rule.CreatedUtc = original.Model.CreatedUtc;
            rule.Enabled = original.Enabled;
            rule.Conditions = original.Model.Conditions;
        }

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
            var existing = original?.Model.Actions.FirstOrDefault(action => action.Type == AutomationActionType.SendNotification);
            rule.Actions.Add(new AutomationAction
            {
                Type = AutomationActionType.SendNotification,
                NotificationTitle = existing?.NotificationTitle ?? "Automation triggered",
                NotificationMessage = existing?.NotificationMessage ?? (string.IsNullOrWhiteSpace(processName)
                    ? $"{GetTriggerLabel(NewTriggerType)} matched. Priority set to {GetPriorityLabel(NewPriority)}."
                    : $"{processName} detected. Priority set to {GetPriorityLabel(NewPriority)}.")
            });
        }

        var row = new AutomationRuleViewModel(rule);
        var rules = original is null
            ? Rules.Select(item => item.Model).Append(rule).ToList()
            : Rules.Select(item => item == original ? rule : item.Model).ToList();
        if (!await SaveRulesAsync(rules, cancellationToken))
        {
            return;
        }

        if (original is null)
        {
            Rules.Add(row);
        }
        else
        {
            Rules[Rules.IndexOf(original)] = row;
        }
        SelectedRule = row;
        IsCreatingRule = false;
        NewProcessName = string.Empty;
        NewWindowTitleContains = string.Empty;
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

    private void ApplyAffinityPreset(long affinityMask)
    {
        NewAffinityEnabled = true;
        NewAffinityMask = $"0x{affinityMask:X}";
    }

    private static long CreateAllCpusMask()
    {
        var processorCount = Math.Clamp(Environment.ProcessorCount, 1, 62);
        return (1L << processorCount) - 1;
    }

    private static long CreateHalfCpusMask(bool firstHalf)
    {
        var processorCount = Math.Clamp(Environment.ProcessorCount, 1, 62);
        var half = Math.Max(1, processorCount / 2);
        if (firstHalf)
        {
            return (1L << half) - 1;
        }

        var upperCount = processorCount - half;
        return ((1L << upperCount) - 1) << half;
    }

    private string? GetPreferredPowerPlanName()
    {
        return AvailablePowerPlans.FirstOrDefault(static plan => plan.Contains("Ultimate", StringComparison.OrdinalIgnoreCase))
            ?? AvailablePowerPlans.FirstOrDefault(static plan => plan.Contains("High performance", StringComparison.OrdinalIgnoreCase))
            ?? AvailablePowerPlans.FirstOrDefault(static plan => plan.Contains("Performance", StringComparison.OrdinalIgnoreCase))
            ?? AvailablePowerPlans.FirstOrDefault();
    }

    private static string GetTriggerLabel(AutomationTriggerType triggerType)
        => triggerType switch
        {
            AutomationTriggerType.ProcessStarted => "App opens",
            AutomationTriggerType.ProcessExited => "App closes",
            AutomationTriggerType.CpuThreshold => "CPU gets busy",
            AutomationTriggerType.MemoryThreshold => "RAM gets high",
            AutomationTriggerType.WindowTitleDetected => "Window title appears",
            AutomationTriggerType.FullscreenDetected => "Fullscreen app appears",
            _ => triggerType.ToString()
        };

    private static string GetPriorityLabel(ProcessPriorityClass priority)
        => priority switch
        {
            ProcessPriorityClass.Idle => "very low background",
            ProcessPriorityClass.BelowNormal => "lower background",
            ProcessPriorityClass.Normal => "normal",
            ProcessPriorityClass.AboveNormal => "small performance boost",
            ProcessPriorityClass.High => "high performance",
            _ => priority.ToString()
        };

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

        if (await SaveRulesAsync(Rules.Where(rule => rule != selected).Select(rule => rule.Model).ToList(), cancellationToken))
        {
            Rules.Remove(selected);
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await SaveRulesAsync(Rules.Select(rule => rule.Model).ToList(), cancellationToken);
    }

    private async Task<bool> SaveRulesAsync(List<AutomationRule> rules, CancellationToken cancellationToken)
    {
        IsSaving = true;
        try
        {
            SaveStatus = "Saving rules...";
            await _rulePersistenceService.UpdateAsync(store => store.Rules = rules, cancellationToken);
            await _automationEngine.ReloadRulesAsync(cancellationToken);
            SaveStatus = "Rules saved";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            SaveStatus = "Could not save rules";
            _dialogService.ShowWarning("Automation rules", exception.Message);
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
