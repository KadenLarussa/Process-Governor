namespace ProcessGovernor.Models;

public sealed class LogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    public LogSeverity Severity { get; set; } = LogSeverity.Information;

    public string Source { get; set; } = "App";

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }

    public int? ProcessId { get; set; }

    public string? RuleId { get; set; }

    public string LocalTimestamp => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
