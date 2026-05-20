using System.Collections.ObjectModel;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;

namespace ProcessGovernor.ViewModels;

public sealed class StartupDiagnosticsViewModel : ObservableObject
{
    private readonly Dictionary<string, StartupCheckViewModel> _checksById = new(StringComparer.OrdinalIgnoreCase);
    private string _statusMessage = "Preparing startup checks";
    private double _progressValue;
    private int _passedCount;
    private int _warningCount;
    private int _failedCount;

    public ObservableCollection<StartupCheckViewModel> Checks { get; } = [];

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public int PassedCount
    {
        get => _passedCount;
        private set => SetProperty(ref _passedCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => SetProperty(ref _warningCount, value);
    }

    public int FailedCount
    {
        get => _failedCount;
        private set => SetProperty(ref _failedCount, value);
    }

    public void Apply(StartupCheckUpdate update)
    {
        if (!_checksById.TryGetValue(update.Id, out var check))
        {
            check = new StartupCheckViewModel(update.Id, update.Name, update.Description);
            _checksById[update.Id] = check;
            Checks.Add(check);
        }

        check.Apply(update);
        ProgressValue = Math.Clamp(update.Progress, 0, 100);
        StatusMessage = update.Status switch
        {
            StartupCheckStatus.Running => update.Name,
            StartupCheckStatus.Warning => $"{update.Name}: warning",
            StartupCheckStatus.Failed => $"{update.Name}: failed",
            _ => update.Detail
        };
        RefreshCounts();
    }

    private void RefreshCounts()
    {
        PassedCount = Checks.Count(static check => check.Status == StartupCheckStatus.Passed);
        WarningCount = Checks.Count(static check => check.Status == StartupCheckStatus.Warning);
        FailedCount = Checks.Count(static check => check.Status == StartupCheckStatus.Failed);
    }
}
