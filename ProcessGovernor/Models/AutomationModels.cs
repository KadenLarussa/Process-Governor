using System.Diagnostics;
using ProcessGovernor.Core;

namespace ProcessGovernor.Models;

public enum AutomationTriggerType
{
    ProcessStarted,
    ProcessExited,
    CpuThreshold,
    MemoryThreshold,
    SystemIdle,
    AppDetected,
    WindowTitleDetected,
    FullscreenDetected
}

public enum AutomationActionType
{
    SetProcessPriority,
    SetCpuAffinity,
    SendNotification,
    LaunchApplication,
    CloseApplication,
    RunPowerShellCommand,
    ChangePowerPlan,
    MuteApplication,
    SuspendProcess,
    ResumeProcess,
    PauseMonitoring
}

public sealed class AutomationTrigger
{
    public AutomationTriggerType Type { get; set; } = AutomationTriggerType.ProcessStarted;

    public string? ProcessName { get; set; }

    public double? Threshold { get; set; }

    public string? WindowTitleContains { get; set; }
}

public sealed class AutomationCondition
{
    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }
}

public sealed class AutomationAction
{
    public AutomationActionType Type { get; set; } = AutomationActionType.SendNotification;

    public string? TargetProcessName { get; set; }

    public ProcessPriorityClass? Priority { get; set; }

    public long? CpuAffinityMask { get; set; }

    public string? NotificationTitle { get; set; }

    public string? NotificationMessage { get; set; }

    public string? ExecutablePath { get; set; }

    public string? Arguments { get; set; }

    public string? PowerShellCommand { get; set; }

    public string? PowerPlanName { get; set; }
}

public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New Rule";

    public bool Enabled { get; set; } = true;

    public AutomationTrigger Trigger { get; set; } = new();

    public List<AutomationCondition> Conditions { get; set; } = [];

    public List<AutomationAction> Actions { get; set; } = [];

    public int CooldownSeconds { get; set; } = 30;

    public int DelaySeconds { get; set; }

    public bool RevertOnExit { get; set; } = true;

    public bool DryRun { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AutomationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New Profile";

    public bool Enabled { get; set; } = true;

    public bool IsActive { get; set; }

    public int PriorityOrder { get; set; }

    public string? AutoActivateProcessName { get; set; }

    public DateTimeOffset? TemporaryOverrideUntilUtc { get; set; }

    public List<string> RuleIds { get; set; } = [];
}

public sealed class PowerPlanInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public override string ToString() => Name;
}

public sealed class AutomationStoreFile
{
    public int SchemaVersion { get; set; } = AppConstants.ConfigSchemaVersion;

    public List<AutomationRule> Rules { get; set; } = [];

    public List<AutomationProfile> Profiles { get; set; } = [];
}
