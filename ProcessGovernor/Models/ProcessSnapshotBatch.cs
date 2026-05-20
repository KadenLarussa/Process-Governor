namespace ProcessGovernor.Models;

public sealed class ProcessSnapshotBatch
{
    public ProcessSnapshotBatch(
        IReadOnlyList<ProcessSnapshot> processes,
        SystemSummary summary,
        IReadOnlySet<int> startedProcessIds,
        IReadOnlySet<int> exitedProcessIds,
        WindowSnapshot? foregroundWindow)
    {
        Processes = processes;
        Summary = summary;
        StartedProcessIds = startedProcessIds;
        ExitedProcessIds = exitedProcessIds;
        ForegroundWindow = foregroundWindow;
    }

    public IReadOnlyList<ProcessSnapshot> Processes { get; }

    public SystemSummary Summary { get; }

    public IReadOnlySet<int> StartedProcessIds { get; }

    public IReadOnlySet<int> ExitedProcessIds { get; }

    public WindowSnapshot? ForegroundWindow { get; }
}
