using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;

namespace ProcessGovernor.ViewModels;

public sealed class AutomationRuleViewModel : ObservableObject
{
    private readonly AutomationRule _model;

    public AutomationRuleViewModel(AutomationRule model)
    {
        _model = model;
    }

    public AutomationRule Model => _model;

    public string Id => _model.Id;

    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name == value)
            {
                return;
            }

            _model.Name = value;
            OnPropertyChanged();
        }
    }

    public bool Enabled
    {
        get => _model.Enabled;
        set
        {
            if (_model.Enabled == value)
            {
                return;
            }

            _model.Enabled = value;
            OnPropertyChanged();
        }
    }

    public bool DryRun
    {
        get => _model.DryRun;
        set
        {
            if (_model.DryRun == value)
            {
                return;
            }

            _model.DryRun = value;
            OnPropertyChanged();
        }
    }

    public bool RevertOnExit
    {
        get => _model.RevertOnExit;
        set
        {
            if (_model.RevertOnExit == value)
            {
                return;
            }

            _model.RevertOnExit = value;
            OnPropertyChanged();
        }
    }

    public string TriggerSummary => _model.Trigger.Type switch
    {
        AutomationTriggerType.ProcessStarted => $"When {_model.Trigger.ProcessName} starts",
        AutomationTriggerType.ProcessExited => $"When {_model.Trigger.ProcessName} exits",
        AutomationTriggerType.CpuThreshold => $"CPU >= {_model.Trigger.Threshold:0.#}% for {_model.Trigger.ProcessName ?? "any process"}",
        AutomationTriggerType.MemoryThreshold => $"RAM >= {_model.Trigger.Threshold:0.#} MB for {_model.Trigger.ProcessName ?? "any process"}",
        _ => _model.Trigger.Type.ToString()
    };

    public string ActionSummary
    {
        get
        {
            if (_model.Actions.Count == 0)
            {
                return "No actions";
            }

            return string.Join(", ", _model.Actions.Select(action => action.Type switch
            {
                AutomationActionType.SetProcessPriority => $"Set priority {action.Priority}",
                AutomationActionType.SetCpuAffinity => $"Set affinity 0x{action.CpuAffinityMask:X}",
                AutomationActionType.ChangePowerPlan => $"Power plan {action.PowerPlanName}",
                AutomationActionType.SuspendProcess => $"Suspend {action.TargetProcessName}",
                AutomationActionType.ResumeProcess => $"Resume {action.TargetProcessName}",
                AutomationActionType.SendNotification => "Notify",
                _ => action.Type.ToString()
            }));
        }
    }
}
