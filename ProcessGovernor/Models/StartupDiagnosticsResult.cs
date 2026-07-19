namespace ProcessGovernor.Models;

public sealed class StartupDiagnosticsResult
{
    public bool Succeeded { get; init; }

    public int PassedCount { get; init; }

    public int WarningCount { get; init; }

    public int FailedCount { get; init; }

    public TimeSpan Duration { get; init; }

    public string Summary { get; init; } = string.Empty;
}
