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
            OnPropertyChanged(nameof(EnabledDisplay));
        }
    }

    public string EnabledDisplay => Enabled ? "On" : "Off";

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
            OnPropertyChanged(nameof(DryRunDisplay));
        }
    }

    public string DryRunDisplay => DryRun ? "Test" : "Live";

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
            OnPropertyChanged(nameof(RollbackDisplay));
        }
    }

    public string RollbackDisplay => RevertOnExit ? "Undo" : "Keep";

    public string TriggerSummary => GetTriggerSummary(_model);

    public string ActionSummary
    {
        get
        {
            if (_model.Actions.Count == 0)
            {
                return "No actions";
            }

            return GetActionSummary(_model);
        }
    }

    public static string GetTriggerSummary(AutomationRule rule)
        => rule.Trigger.Type switch
        {
            AutomationTriggerType.ProcessStarted => $"App opens: {rule.Trigger.ProcessName}",
            AutomationTriggerType.ProcessExited => $"App closes: {rule.Trigger.ProcessName}",
            AutomationTriggerType.CpuThreshold => $"CPU gets busy: {rule.Trigger.ProcessName ?? "any app"} at {rule.Trigger.Threshold:0.#}%",
            AutomationTriggerType.MemoryThreshold => $"RAM gets high: {rule.Trigger.ProcessName ?? "any app"} at {rule.Trigger.Threshold:0.#} MB",
            AutomationTriggerType.WindowTitleDetected => $"Window title: \"{rule.Trigger.WindowTitleContains}\"",
            AutomationTriggerType.FullscreenDetected => string.IsNullOrWhiteSpace(rule.Trigger.ProcessName)
                ? "Fullscreen app appears"
                : $"Fullscreen: {rule.Trigger.ProcessName}",
            _ => rule.Trigger.Type.ToString()
        };

    public static string GetActionSummary(AutomationRule rule)
    {
        if (rule.Actions.Count == 0)
        {
            return "No actions";
        }

        return string.Join(", ", rule.Actions.Select(action => action.Type switch
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
