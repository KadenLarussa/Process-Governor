using System.Collections.ObjectModel;
using ProcessGovernor.Core;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Services;

namespace ProcessGovernor.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AutomationsViewModel _automationsViewModel;
    private readonly ProfilesViewModel _profilesViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private PageNavigationItem? _selectedNavigationItem;
    private object? _currentPage;

    public MainViewModel(
        DashboardViewModel dashboardViewModel,
        AutomationsViewModel automationsViewModel,
        ProfilesViewModel profilesViewModel,
        LogsViewModel logsViewModel,
        SettingsViewModel settingsViewModel,
        IElevationService elevationService)
    {
        _automationsViewModel = automationsViewModel;
        _profilesViewModel = profilesViewModel;
        _logsViewModel = logsViewModel;
        _settingsViewModel = settingsViewModel;
        IsElevated = elevationService.IsElevated;

        NavigationItems =
        [
            new PageNavigationItem("Dashboard", dashboardViewModel, "D"),
            new PageNavigationItem("Automations", automationsViewModel, "A"),
            new PageNavigationItem("Profiles", profilesViewModel, "P"),
            new PageNavigationItem("Logs", logsViewModel, "L"),
            new PageNavigationItem("Settings", settingsViewModel, "S")
        ];

        SelectedNavigationItem = NavigationItems[0];
    }

    public string AppName => AppConstants.AppName;

    public bool IsElevated { get; }

    public string ElevationDisplay => IsElevated ? "Administrator" : "Standard user";

    public ObservableCollection<PageNavigationItem> NavigationItems { get; }

    public PageNavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (SetProperty(ref _selectedNavigationItem, value))
            {
                CurrentPage = value?.ViewModel;
            }
        }
    }

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _automationsViewModel.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _profilesViewModel.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _logsViewModel.LoadAsync(cancellationToken).ConfigureAwait(false);
    }
}
