using System.Text;
using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly AppPaths _paths;
    private readonly IJsonFileStore _store;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<LogEntry> _entries = [];

    public LoggingService(AppPaths paths, IJsonFileStore store, ISettingsService settingsService)
    {
        _paths = paths;
        _store = store;
        _settingsService = settingsService;
    }

    public event EventHandler<LogEntry>? EntryAdded;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var logStore = await _store.LoadAsync(_paths.GetLogPath("events.json"), static () => new LogStoreFile(), cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _entries.Clear();
            _entries.AddRange(logStore.Entries.OrderBy(static entry => entry.TimestampUtc));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<LogEntry>> GetEntriesAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _entries.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task LogAsync(
        LogSeverity severity,
        string source,
        string message,
        string? details = null,
        int? processId = null,
        string? ruleId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new LogEntry
        {
            Severity = severity,
            Source = source,
            Message = message,
            Details = details,
            ProcessId = processId,
            RuleId = ruleId,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _entries.Add(entry);
            var maxEntries = Math.Clamp(_settingsService.Current.MaxLogEntries, 100, 100_000);
            if (_entries.Count > maxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - maxEntries);
            }

            await PersistLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        EntryAdded?.Invoke(this, entry);
    }

    public async Task ExportJsonAsync(string path, CancellationToken cancellationToken)
    {
        var entries = await GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        await _store.SaveAsync(path, new LogStoreFile { Entries = entries.ToList() }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportCsvAsync(string path, CancellationToken cancellationToken)
    {
        var entries = await GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();
        builder.AppendLine("TimestampUtc,Severity,Source,ProcessId,RuleId,Message,Details");

        foreach (var entry in entries)
        {
            builder
                .Append(Escape(entry.TimestampUtc.ToString("O"))).Append(',')
                .Append(Escape(entry.Severity.ToString())).Append(',')
                .Append(Escape(entry.Source)).Append(',')
                .Append(Escape(entry.ProcessId?.ToString() ?? string.Empty)).Append(',')
                .Append(Escape(entry.RuleId ?? string.Empty)).Append(',')
                .Append(Escape(entry.Message)).Append(',')
                .AppendLine(Escape(entry.Details ?? string.Empty));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistLockedAsync(CancellationToken cancellationToken)
    {
        await _store.SaveAsync(_paths.GetLogPath("events.json"), new LogStoreFile { Entries = _entries.ToList() }, cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}
