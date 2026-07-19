using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;

namespace ProcessGovernor.ViewModels;

public sealed class StartupCheckViewModel : ObservableObject
{
    private StartupCheckStatus _status = StartupCheckStatus.Pending;
    private string _detail = "Waiting";
    private TimeSpan _duration;

    public StartupCheckViewModel(string id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public StartupCheckStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        private set
        {
            if (SetProperty(ref _duration, value))
            {
                OnPropertyChanged(nameof(DurationText));
            }
        }
    }

    public string StatusText => Status switch
    {
        StartupCheckStatus.Pending => "WAIT",
        StartupCheckStatus.Running => "RUN",
        StartupCheckStatus.Passed => "PASS",
        StartupCheckStatus.Warning => "WARN",
        StartupCheckStatus.Failed => "FAIL",
        _ => "WAIT"
    };

    public System.Windows.Media.Brush StatusBrush => Status switch
    {
        StartupCheckStatus.Running => System.Windows.Media.Brushes.DeepSkyBlue,
        StartupCheckStatus.Passed => System.Windows.Media.Brushes.MediumSeaGreen,
        StartupCheckStatus.Warning => System.Windows.Media.Brushes.Goldenrod,
        StartupCheckStatus.Failed => System.Windows.Media.Brushes.IndianRed,
        _ => System.Windows.Media.Brushes.SlateGray
    };

    public string DurationText => Duration == TimeSpan.Zero ? string.Empty : $"{Duration.TotalMilliseconds:0} ms";

    public void Apply(StartupCheckUpdate update)
    {
        Status = update.Status;
        Detail = update.Detail;
        Duration = update.Duration;
    }
}
