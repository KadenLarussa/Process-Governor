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
    private readonly IDialogService _dialogService;
    private AutomationStoreFile _store = new();
    private AutomationRuleViewModel? _selectedRule;
    private string _newRuleName = "New Process Rule";
    private string _newProcessName = string.Empty;
    private ProcessPriorityClass _newPriority = ProcessPriorityClass.High;
    private bool _newNotificationEnabled = true;
    private bool _newRevertOnExit = true;
    private bool _newDryRun;
    private int _newCooldownSeconds = 30;
    private int _newDelaySeconds;

    public AutomationsViewModel(
        IRulePersistenceService rulePersistenceService,
        IAutomationEngine automationEngine,
        IDialogService dialogService)
    {
        _rulePersistenceService = rulePersistenceService;
        _automationEngine = automationEngine;
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

    public ProcessPriorityClass NewPriority
    {
        get => _newPriority;
        set => SetProperty(ref _newPriority, value);
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
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Rules.Clear();
            foreach (var rule in _store.Rules.OrderBy(static rule => rule.Name))
            {
                Rules.Add(new AutomationRuleViewModel(rule));
            }
        });
    }

    private async Task AddRuleAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(NewProcessName))
        {
            _dialogService.ShowWarning("Automation Rule", "Enter the process name to watch, for example cs2.exe.");
            return;
        }

        var processName = NewProcessName.Trim();
        var rule = new AutomationRule
        {
            Name = string.IsNullOrWhiteSpace(NewRuleName) ? $"{processName} priority" : NewRuleName.Trim(),
            Enabled = true,
            Trigger = new AutomationTrigger
            {
                Type = AutomationTriggerType.ProcessStarted,
                ProcessName = processName
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
                    TargetProcessName = processName,
                    Priority = NewPriority
                }
            ]
        };

        if (NewNotificationEnabled)
        {
            rule.Actions.Add(new AutomationAction
            {
                Type = AutomationActionType.SendNotification,
                NotificationTitle = "Automation triggered",
                NotificationMessage = $"{processName} detected. Priority set to {NewPriority}."
            });
        }

        _store.Rules.Add(rule);
        Rules.Add(new AutomationRuleViewModel(rule));
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        NewProcessName = string.Empty;
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
