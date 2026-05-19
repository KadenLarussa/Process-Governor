using System.Collections.ObjectModel;
using System.Diagnostics;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.Services;

namespace ProcessGovernor.ViewModels;

public sealed class AutomationsViewModel : ObservableObject
{
    private const string SafePcBoostRuleName = "Safe PC Boost: busy app helper";
    private const string SafePcBoostProfileName = "Safe PC Boost";
    private const string LegacyFocusedRuleName = "Focused Performance: high-load app boost";
    private const string LegacyFocusedProfileName = "Focused Performance";
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
    private string _presetStatus = "Safe presets are reversible, logged, and only run when their profile is active.";

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
        AddFocusedPerformanceProfileCommand = new AsyncRelayCommand(AddFocusedPerformanceProfileAsync);
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
        new(AutomationTriggerType.MemoryThreshold, "RAM gets high", "Run when an app crosses a RAM amount.")
    ];

    public ObservableCollection<string> AvailablePowerPlans { get; } = [];

    public AsyncRelayCommand AddRuleCommand { get; }

    public AsyncRelayCommand DeleteRuleCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand ReloadCommand { get; }

    public RelayCommand UseGameLaunchPresetCommand { get; }

    public RelayCommand UseFocusedPerformancePresetCommand { get; }

    public RelayCommand UseQuietBackgroundPresetCommand { get; }

    public AsyncRelayCommand AddFocusedPerformanceProfileCommand { get; }

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
        set
        {
            if (SetProperty(ref _newTriggerType, value))
            {
                OnPropertyChanged(nameof(NewTriggerHelp));
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

    public string PresetStatus
    {
        get => _presetStatus;
        set => SetProperty(ref _presetStatus, value);
    }

    public string NewTriggerHelp => NewTriggerType switch
    {
        AutomationTriggerType.ProcessStarted => "Best for games and apps. Type the executable name, for example cs2.exe.",
        AutomationTriggerType.ProcessExited => "Useful for cleanup rules after an app closes.",
        AutomationTriggerType.CpuThreshold => "Watches CPU load and boosts the matching app only after it gets busy.",
        AutomationTriggerType.MemoryThreshold => "Watches RAM usage. Threshold is entered in MB.",
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

            NewPowerPlanName ??= GetPreferredPowerPlanName();
        });
    }

    private void UseGameLaunchPreset()
    {
        NewRuleName = "Game Launch Boost";
        NewProcessName = "game.exe";
        NewTriggerType = AutomationTriggerType.ProcessStarted;
        NewThresholdValue = 80;
        NewPriority = ProcessPriorityClass.High;
        NewAffinityEnabled = false;
        NewAffinityMask = string.Empty;
        NewPowerPlanEnabled = true;
        NewPowerPlanName = GetPreferredPowerPlanName();
        NewNotificationEnabled = true;
        NewRevertOnExit = true;
        NewDryRun = false;
        NewCooldownSeconds = 30;
        NewDelaySeconds = 1;
        PresetStatus = "Game Launch Boost is ready. Replace game.exe with your game, then add and save the rule.";
    }

    private void UseFocusedPerformancePreset()
    {
        NewRuleName = SafePcBoostRuleName;
        NewProcessName = string.Empty;
        NewTriggerType = AutomationTriggerType.CpuThreshold;
        NewThresholdValue = 55;
        NewPriority = ProcessPriorityClass.AboveNormal;
        NewAffinityEnabled = false;
        NewAffinityMask = string.Empty;
        NewPowerPlanEnabled = true;
        NewPowerPlanName = GetPreferredPowerPlanName();
        NewNotificationEnabled = true;
        NewRevertOnExit = true;
        NewDryRun = false;
        NewCooldownSeconds = 180;
        NewDelaySeconds = 0;
        PresetStatus = "Safe PC Boost is ready. It gives busy apps a small, logged boost and rolls back when possible.";
    }

    private void UseQuietBackgroundPreset()
    {
        NewRuleName = "Quiet Background App";
        NewProcessName = "discord.exe";
        NewTriggerType = AutomationTriggerType.ProcessStarted;
        NewThresholdValue = 80;
        NewPriority = ProcessPriorityClass.BelowNormal;
        NewAffinityEnabled = false;
        NewAffinityMask = string.Empty;
        NewPowerPlanEnabled = false;
        NewNotificationEnabled = true;
        NewRevertOnExit = true;
        NewDryRun = false;
        NewCooldownSeconds = 60;
        NewDelaySeconds = 0;
        PresetStatus = "Quiet Background is ready. Replace discord.exe with an app you want to make less pushy.";
    }

    private async Task AddFocusedPerformanceProfileAsync(CancellationToken cancellationToken)
    {
        var rule = _store.Rules.FirstOrDefault(static rule =>
            rule.Name.Equals(SafePcBoostRuleName, StringComparison.OrdinalIgnoreCase)
            || rule.Name.Equals(LegacyFocusedRuleName, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            rule = CreateFocusedPerformanceRule(GetPreferredPowerPlanName() ?? "High performance");
            _store.Rules.Add(rule);
        }
        else if (rule.Name.Equals(LegacyFocusedRuleName, StringComparison.OrdinalIgnoreCase))
        {
            rule.Name = SafePcBoostRuleName;
        }

        var profile = _store.Profiles.FirstOrDefault(static profile => profile.Name.Equals(SafePcBoostProfileName, StringComparison.OrdinalIgnoreCase))
            ?? _store.Profiles.FirstOrDefault(static profile => profile.Name.Equals(LegacyFocusedProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            profile = new AutomationProfile
            {
                Name = SafePcBoostProfileName,
                PriorityOrder = 5,
                Enabled = true
            };
            _store.Profiles.Add(profile);
        }
        else if (profile.Name.Equals(LegacyFocusedProfileName, StringComparison.OrdinalIgnoreCase)
            && !_store.Profiles.Any(static item => item.Name.Equals(SafePcBoostProfileName, StringComparison.OrdinalIgnoreCase)))
        {
            profile.Name = SafePcBoostProfileName;
        }

        if (!profile.RuleIds.Contains(rule.Id, StringComparer.OrdinalIgnoreCase))
        {
            profile.RuleIds.Add(rule.Id);
        }

        await _rulePersistenceService.SaveAsync(_store, cancellationToken).ConfigureAwait(false);
        await _automationEngine.ReloadRulesAsync(cancellationToken).ConfigureAwait(false);
        await LoadAsync(cancellationToken).ConfigureAwait(false);
        PresetStatus = "Safe PC Boost profile is installed. Activate it from Profiles when you want the boost armed.";
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
            Name = string.IsNullOrWhiteSpace(NewRuleName) ? $"{GetTriggerLabel(NewTriggerType)} rule" : NewRuleName.Trim(),
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
                    ? $"{GetTriggerLabel(NewTriggerType)} threshold met. Priority set to {GetPriorityLabel(NewPriority)}."
                    : $"{processName} detected. Priority set to {GetPriorityLabel(NewPriority)}."
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

    private static AutomationRule CreateFocusedPerformanceRule(string powerPlanName)
    {
        return new AutomationRule
        {
            Name = SafePcBoostRuleName,
            Enabled = true,
            Trigger = new AutomationTrigger
            {
                Type = AutomationTriggerType.CpuThreshold,
                Threshold = 55
            },
            RevertOnExit = true,
            CooldownSeconds = 180,
            Actions =
            [
                new AutomationAction
                {
                    Type = AutomationActionType.SetProcessPriority,
                    Priority = ProcessPriorityClass.AboveNormal
                },
                new AutomationAction
                {
                    Type = AutomationActionType.ChangePowerPlan,
                    PowerPlanName = powerPlanName
                },
                new AutomationAction
                {
                    Type = AutomationActionType.SendNotification,
                    NotificationTitle = "Safe PC Boost",
                    NotificationMessage = "A high-load process was detected and given a temporary priority boost."
                }
            ]
        };
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
