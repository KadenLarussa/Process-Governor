using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class RuleEvaluationService : IRuleEvaluationService
{
    public bool IsMatch(AutomationRule rule, ProcessSnapshot process, AutomationTriggerType triggerType)
    {
        if (!rule.Enabled || rule.Trigger.Type != triggerType)
        {
            return false;
        }

        return triggerType switch
        {
            AutomationTriggerType.ProcessStarted or AutomationTriggerType.ProcessExited
                => MatchesProcessName(rule.Trigger.ProcessName, process.Name),
            _ => false
        };
    }

    public static bool MatchesProcessName(string? configuredName, string processName)
    {
        if (string.IsNullOrWhiteSpace(configuredName))
        {
            return false;
        }

        return Normalize(configuredName).Equals(Normalize(processName), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        var trimmed = value.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
