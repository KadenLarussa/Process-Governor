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
            new PageNavigationItem("Dashboard", dashboardViewModel, "\uE80F"),
            new PageNavigationItem("Automations", automationsViewModel, "\uE768"),
            new PageNavigationItem("Profiles", profilesViewModel, "\uE77B"),
            new PageNavigationItem("Logs", logsViewModel, "\uE8A5"),
            new PageNavigationItem("Settings", settingsViewModel, "\uE713")
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
                if (ReferenceEquals(value?.ViewModel, _profilesViewModel))
                {
                    _profilesViewModel.UpdateRuleCatalog(_automationsViewModel.Rules.Select(rule => rule.Model));
                }

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
