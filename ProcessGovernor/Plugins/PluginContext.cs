using ProcessGovernor.Services;

namespace ProcessGovernor.Plugins;

public sealed class PluginContext
{
    public required ILoggingService Logging { get; init; }

    public required INotificationService Notifications { get; init; }

    public required IProcessActionService ProcessActions { get; init; }

    public required IPowerPlanService PowerPlans { get; init; }
}
