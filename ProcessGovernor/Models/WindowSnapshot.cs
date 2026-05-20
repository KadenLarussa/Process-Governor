namespace ProcessGovernor.Models;

public sealed class WindowSnapshot
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public bool IsFullscreen { get; init; }

    public long WindowHandle { get; init; }
}
