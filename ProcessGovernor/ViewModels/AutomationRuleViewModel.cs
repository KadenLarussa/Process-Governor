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
        AutomationTriggerType.ProcessStarted => $"App opens: {_model.Trigger.ProcessName}",
        AutomationTriggerType.ProcessExited => $"App closes: {_model.Trigger.ProcessName}",
        AutomationTriggerType.CpuThreshold => $"CPU gets busy: {_model.Trigger.ProcessName ?? "any app"} at {_model.Trigger.Threshold:0.#}%",
        AutomationTriggerType.MemoryThreshold => $"RAM gets high: {_model.Trigger.ProcessName ?? "any app"} at {_model.Trigger.Threshold:0.#} MB",
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
                AutomationActionType.SetProcessPriority => $"Priority -> {GetPriorityLabel(action.Priority)}",
                AutomationActionType.SetCpuAffinity => $"CPU cores -> 0x{action.CpuAffinityMask:X}",
                AutomationActionType.ChangePowerPlan => $"Power plan -> {action.PowerPlanName}",
                AutomationActionType.SuspendProcess => $"Pause app -> {action.TargetProcessName}",
                AutomationActionType.ResumeProcess => $"Resume app -> {action.TargetProcessName}",
                AutomationActionType.SendNotification => "Show notification",
                _ => action.Type.ToString()
            }));
        }
    }

    private static string GetPriorityLabel(System.Diagnostics.ProcessPriorityClass? priority)
        => priority switch
        {
            System.Diagnostics.ProcessPriorityClass.Idle => "very low",
            System.Diagnostics.ProcessPriorityClass.BelowNormal => "lower",
            System.Diagnostics.ProcessPriorityClass.Normal => "normal",
            System.Diagnostics.ProcessPriorityClass.AboveNormal => "small boost",
            System.Diagnostics.ProcessPriorityClass.High => "high",
            null => "normal",
            _ => priority.ToString() ?? "normal"
        };
}
