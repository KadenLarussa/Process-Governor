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
    private int _temporaryOverrideMinutes = 30;

    public ProfilesViewModel(IRulePersistenceService rulePersistenceService, IAutomationEngine automationEngine)
    {
        _rulePersistenceService = rulePersistenceService;
        _automationEngine = automationEngine;
        AddProfileCommand = new AsyncRelayCommand(AddProfileAsync);
        DeleteProfileCommand = new AsyncRelayCommand(DeleteProfileAsync, () => SelectedProfile is not null);
        ActivateProfileCommand = new AsyncRelayCommand(ActivateProfileAsync, () => SelectedProfile is not null);
        ActivateTemporaryCommand = new AsyncRelayCommand(ActivateTemporaryAsync, () => SelectedProfile is not null);
        ClearActiveProfilesCommand = new AsyncRelayCommand(ClearActiveProfilesAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ReloadCommand = new AsyncRelayCommand(LoadAsync);
    }

    public ObservableCollection<AutomationProfile> Profiles { get; } = [];

    public ObservableCollection<ProfileRuleAssignmentViewModel> RuleAssignments { get; } = [];

    public AsyncRelayCommand AddProfileCommand { get; }

    public AsyncRelayCommand DeleteProfileCommand { get; }

    public AsyncRelayCommand ActivateProfileCommand { get; }

    public AsyncRelayCommand ActivateTemporaryCommand { get; }

    public AsyncRelayCommand ClearActiveProfilesCommand { get; }

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
                ActivateProfileCommand.RaiseCanExecuteChanged();
                ActivateTemporaryCommand.RaiseCanExecuteChanged();
                RefreshRuleAssignments();
                OnPropertyChanged(nameof(SelectedProfileName));
            }
        }
    }

    public string SelectedProfileName => SelectedProfile?.Name ?? "Select a profile";

    public string NewProfileName
    {
        get => _newProfileName;
        set => SetProperty(ref _newProfileName, value);
    }

    public int TemporaryOverrideMinutes
    {
        get => _temporaryOverrideMinutes;
        set => SetProperty(ref _temporaryOverrideMinutes, Math.Clamp(value, 1, 1440));
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _store = await _rulePersistenceService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var selectedProfileId = SelectedProfile?.Id;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Profiles.Clear();
            foreach (var profile in _store.Profiles.OrderBy(static profile => profile.PriorityOrder).ThenBy(static profile => profile.Name))
            {
                Profiles.Add(profile);
            }

            SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == selectedProfileId) ?? Profiles.FirstOrDefault();
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

    private async Task ActivateProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        foreach (var profile in Profiles)
        {
            profile.IsActive = profile.Id == SelectedProfile.Id;
            profile.TemporaryOverrideUntilUtc = null;
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ActivateTemporaryAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var until = DateTimeOffset.UtcNow.AddMinutes(TemporaryOverrideMinutes);
        foreach (var profile in Profiles)
        {
            profile.IsActive = false;
            profile.TemporaryOverrideUntilUtc = profile.Id == SelectedProfile.Id ? until : null;
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ClearActiveProfilesAsync(CancellationToken cancellationToken)
    {
        foreach (var profile in Profiles)
        {
            profile.IsActive = false;
            profile.TemporaryOverrideUntilUtc = null;
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        _store.Profiles = Profiles.ToList();
        await _rulePersistenceService.SaveAsync(_store, cancellationToken).ConfigureAwait(false);
        await _automationEngine.ReloadRulesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RefreshRuleAssignments()
    {
        RuleAssignments.Clear();
        if (SelectedProfile is null)
        {
            return;
        }

        foreach (var rule in _store.Rules.OrderBy(static rule => rule.Name))
        {
            RuleAssignments.Add(new ProfileRuleAssignmentViewModel(SelectedProfile, rule));
        }
    }
}
