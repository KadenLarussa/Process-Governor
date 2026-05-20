using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using ProcessGovernor.Core;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.Services;

namespace ProcessGovernor.ViewModels;

public sealed class LogsViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    private readonly AppPaths _paths;
    private string _searchText = string.Empty;
    private string _severityFilter = "All";

    public LogsViewModel(ILoggingService loggingService, AppPaths paths)
    {
        _loggingService = loggingService;
        _paths = paths;
        LogsView = CollectionViewSource.GetDefaultView(Entries);
        LogsView.Filter = FilterLog;
        LogsView.SortDescriptions.Add(new SortDescription(nameof(LogEntry.TimestampUtc), ListSortDirection.Descending));

        ExportJsonCommand = new AsyncRelayCommand(ExportJsonAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync);

        _loggingService.EntryAdded += OnEntryAdded;
    }

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public ICollectionView LogsView { get; }

    public IReadOnlyList<string> SeverityOptions { get; } = ["All", "Trace", "Information", "Warning", "Error"];

    public AsyncRelayCommand ExportJsonCommand { get; }

    public AsyncRelayCommand ExportCsvCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public string SeverityFilter
    {
        get => _severityFilter;
        set
        {
            if (SetProperty(ref _severityFilter, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _loggingService.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        await UiDispatch.InvokeAsync(() =>
        {
            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }
        }).ConfigureAwait(false);
    }

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        _ = UiDispatch.InvokeAsync(() =>
        {
            Entries.Add(entry);
            LogsView.Refresh();
        });
    }

    private bool FilterLog(object item)
    {
        if (item is not LogEntry entry)
        {
            return false;
        }

        if (!SeverityFilter.Equals("All", StringComparison.OrdinalIgnoreCase)
            && !entry.Severity.ToString().Equals(SeverityFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Source.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (entry.Details?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.ProcessId?.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private Task ExportJsonAsync(CancellationToken cancellationToken)
    {
        var path = _paths.GetLogPath($"events-export-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        return _loggingService.ExportJsonAsync(path, cancellationToken);
    }

    private Task ExportCsvAsync(CancellationToken cancellationToken)
    {
        var path = _paths.GetLogPath($"events-export-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        return _loggingService.ExportCsvAsync(path, cancellationToken);
    }

}
