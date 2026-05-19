using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.Services;

namespace ProcessGovernor.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly IProcessMonitorService _processMonitorService;
    private readonly IProcessActionService _processActionService;
    private readonly IDialogService _dialogService;
    private readonly Dictionary<int, ProcessRowViewModel> _rowsByPid = new();
    private string _searchText = string.Empty;
    private SystemSummary _summary = new();

    public DashboardViewModel(
        IProcessMonitorService processMonitorService,
        IProcessActionService processActionService,
        IDialogService dialogService)
    {
        _processMonitorService = processMonitorService;
        _processActionService = processActionService;
        _dialogService = dialogService;

        ProcessesView = CollectionViewSource.GetDefaultView(Processes);
        ProcessesView.Filter = FilterProcess;
        ProcessesView.SortDescriptions.Add(new SortDescription(nameof(ProcessRowViewModel.CpuUsagePercent), ListSortDirection.Descending));

        KillProcessCommand = new AsyncRelayCommand((parameter, token) => KillProcessAsync(parameter, false, token));
        EndProcessTreeCommand = new AsyncRelayCommand((parameter, token) => KillProcessAsync(parameter, true, token));
        OpenFileLocationCommand = new AsyncRelayCommand(OpenFileLocationAsync);
        CopyPathCommand = new AsyncRelayCommand(CopyPathAsync);
        SetIdlePriorityCommand = new AsyncRelayCommand((parameter, token) => SetPriorityAsync(parameter, ProcessPriorityClass.Idle, token));
        SetBelowNormalPriorityCommand = new AsyncRelayCommand((parameter, token) => SetPriorityAsync(parameter, ProcessPriorityClass.BelowNormal, token));
        SetNormalPriorityCommand = new AsyncRelayCommand((parameter, token) => SetPriorityAsync(parameter, ProcessPriorityClass.Normal, token));
        SetAboveNormalPriorityCommand = new AsyncRelayCommand((parameter, token) => SetPriorityAsync(parameter, ProcessPriorityClass.AboveNormal, token));
        SetHighPriorityCommand = new AsyncRelayCommand((parameter, token) => SetPriorityAsync(parameter, ProcessPriorityClass.High, token));

        _processMonitorService.SnapshotUpdated += OnSnapshotUpdated;
    }

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = [];

    public ICollectionView ProcessesView { get; }

    public AsyncRelayCommand KillProcessCommand { get; }

    public AsyncRelayCommand EndProcessTreeCommand { get; }

    public AsyncRelayCommand OpenFileLocationCommand { get; }

    public AsyncRelayCommand CopyPathCommand { get; }

    public AsyncRelayCommand SetIdlePriorityCommand { get; }

    public AsyncRelayCommand SetBelowNormalPriorityCommand { get; }

    public AsyncRelayCommand SetNormalPriorityCommand { get; }

    public AsyncRelayCommand SetAboveNormalPriorityCommand { get; }

    public AsyncRelayCommand SetHighPriorityCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ProcessesView.Refresh();
            }
        }
    }

    public SystemSummary Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    private void OnSnapshotUpdated(object? sender, ProcessSnapshotBatch batch)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ApplySnapshot(batch));
    }

    private void ApplySnapshot(ProcessSnapshotBatch batch)
    {
        Summary = batch.Summary;
        var livePids = batch.Processes.Select(static process => process.ProcessId).ToHashSet();

        for (var i = Processes.Count - 1; i >= 0; i--)
        {
            if (!livePids.Contains(Processes[i].ProcessId))
            {
                _rowsByPid.Remove(Processes[i].ProcessId);
                Processes.RemoveAt(i);
            }
        }

        foreach (var snapshot in batch.Processes)
        {
            if (_rowsByPid.TryGetValue(snapshot.ProcessId, out var row))
            {
                row.UpdateFrom(snapshot);
                continue;
            }

            row = new ProcessRowViewModel(snapshot);
            _rowsByPid[snapshot.ProcessId] = row;
            Processes.Add(row);
        }

        ProcessesView.Refresh();
    }

    private bool FilterProcess(object item)
    {
        if (item is not ProcessRowViewModel process)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return process.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || process.ProcessId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
            || process.ExecutablePathDisplay.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private async Task KillProcessAsync(object? parameter, bool entireTree, CancellationToken cancellationToken)
    {
        if (parameter is not ProcessRowViewModel process)
        {
            return;
        }

        var force = false;
        if (process.IsCritical)
        {
            force = _dialogService.Confirm(
                "Critical Process",
                $"{process.Name} is a critical or protected Windows process. Ending it can destabilize Windows. Continue?");
            if (!force)
            {
                return;
            }
        }

        var result = await _processActionService.KillAsync(process.ProcessId, entireTree, force, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => _dialogService.ShowError("Process Action Failed", result.Message));
        }
    }

    private async Task SetPriorityAsync(object? parameter, ProcessPriorityClass priority, CancellationToken cancellationToken)
    {
        if (parameter is not ProcessRowViewModel process)
        {
            return;
        }

        var result = await _processActionService.SetPriorityAsync(process.ProcessId, priority, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => _dialogService.ShowError("Priority Change Failed", result.Message));
        }
    }

    private async Task OpenFileLocationAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not ProcessRowViewModel process)
        {
            return;
        }

        var result = await _processActionService.OpenFileLocationAsync(process.ExecutablePath, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => _dialogService.ShowWarning("Open File Location", result.Message));
        }
    }

    private async Task CopyPathAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not ProcessRowViewModel process)
        {
            return;
        }

        var result = await _processActionService.CopyPathAsync(process.ExecutablePath, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => _dialogService.ShowWarning("Copy Path", result.Message));
        }
    }
}
