using ProcessGovernor.Core;

namespace ProcessGovernor.Models;

public sealed class LogStoreFile
{
    public int SchemaVersion { get; set; } = AppConstants.ConfigSchemaVersion;

    public List<LogEntry> Entries { get; set; } = [];
}
