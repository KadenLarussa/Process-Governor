namespace ProcessGovernor.Models;

public sealed class ProcessSnapshotBatch
{
    public ProcessSnapshotBatch(
        IReadOnlyList<ProcessSnapshot> processes,
        SystemSummary summary,
        IReadOnlySet<int> startedProcessIds,
        IReadOnlySet<int> exitedProcessIds)
    {
        Processes = processes;
        Summary = summary;
        StartedProcessIds = startedProcessIds;
        ExitedProcessIds = exitedProcessIds;
    }

    public IReadOnlyList<ProcessSnapshot> Processes { get; }

    public SystemSummary Summary { get; }

    public IReadOnlySet<int> StartedProcessIds { get; }

    public IReadOnlySet<int> ExitedProcessIds { get; }
}
