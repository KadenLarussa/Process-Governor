using System.Collections.ObjectModel;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.Services;

namespace ProcessGovernor.ViewModels;

public sealed class ProfilesViewModel : ObservableObject
{
    private readonly IRulePersistenceService _rulePersistenceService;
    private readonly IAutomationEngine _automationEngine;
    private AutomationStoreFile _store = new();
    private AutomationProfile? _selectedProfile;
    private string _newProfileName = "New Profile";

    public ProfilesViewModel(IRulePersistenceService rulePersistenceService, IAutomationEngine automationEngine)
    {
        _rulePersistenceService = rulePersistenceService;
        _automationEngine = automationEngine;
        AddProfileCommand = new AsyncRelayCommand(AddProfileAsync);
        DeleteProfileCommand = new AsyncRelayCommand(DeleteProfileAsync, () => SelectedProfile is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ReloadCommand = new AsyncRelayCommand(LoadAsync);
    }

    public ObservableCollection<AutomationProfile> Profiles { get; } = [];

    public AsyncRelayCommand AddProfileCommand { get; }

    public AsyncRelayCommand DeleteProfileCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand ReloadCommand { get; }

    public AutomationProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                DeleteProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewProfileName
    {
        get => _newProfileName;
        set => SetProperty(ref _newProfileName, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _store = await _rulePersistenceService.LoadAsync(cancellationToken).ConfigureAwait(false);
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Profiles.Clear();
            foreach (var profile in _store.Profiles.OrderBy(static profile => profile.PriorityOrder).ThenBy(static profile => profile.Name))
            {
                Profiles.Add(profile);
            }
        });
    }

    private async Task AddProfileAsync(CancellationToken cancellationToken)
    {
        var profile = new AutomationProfile
        {
            Name = string.IsNullOrWhiteSpace(NewProfileName) ? "New Profile" : NewProfileName.Trim(),
            PriorityOrder = Profiles.Count == 0 ? 10 : Profiles.Max(static item => item.PriorityOrder) + 10
        };

        _store.Profiles.Add(profile);
        Profiles.Add(profile);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var selected = SelectedProfile;
        _store.Profiles.RemoveAll(profile => profile.Id == selected.Id);
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Profiles.Remove(selected));
        SelectedProfile = null;
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        _store.Profiles = Profiles.ToList();
        await _rulePersistenceService.SaveAsync(_store, cancellationToken).ConfigureAwait(false);
        await _automationEngine.ReloadRulesAsync(cancellationToken).ConfigureAwait(false);
    }
}
