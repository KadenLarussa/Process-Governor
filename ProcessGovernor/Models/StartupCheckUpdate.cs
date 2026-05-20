namespace ProcessGovernor.Models;

public sealed record StartupCheckUpdate(
    string Id,
    string Name,
    string Description,
    StartupCheckStatus Status,
    string Detail,
    TimeSpan Duration,
    double Progress);
