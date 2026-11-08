using System.Collections.ObjectModel;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.Services;

namespace ProcessGovernor.ViewModels;

public sealed class ProfilesViewModel : ObservableObject
{
    private readonly IRulePersistenceService _rulePersistenceService;
    private readonly IAutomationEngine _automationEngine;
    private readonly IDialogService _dialogService;
    private IReadOnlyList<AutomationRule> _rules = [];
    private AutomationProfile? _selectedProfile;
    private string _newProfileName = string.Empty;
    private int _temporaryOverrideMinutes = 30;
    private bool _isSaving;
    private string _saveStatus = string.Empty;

    public ProfilesViewModel(IRulePersistenceService rulePersistenceService, IAutomationEngine automationEngine, IDialogService dialogService)
    {
        _rulePersistenceService = rulePersistenceService;
        _automationEngine = automationEngine;
        _dialogService = dialogService;
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
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedProfile is not null;

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetProperty(ref _isSaving, value);
    }

    public string SaveStatus
    {
        get => _saveStatus;
        private set => SetProperty(ref _saveStatus, value);
    }

    public void UpdateRuleCatalog(IEnumerable<AutomationRule> rules)
    {
        _rules = rules.ToList();
        RefreshRuleAssignments();
    }

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
        var store = await _rulePersistenceService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var selectedProfileId = SelectedProfile?.Id;
        await UiDispatch.InvokeAsync(() =>
        {
            _rules = store.Rules;
            Profiles.Clear();
            foreach (var profile in store.Profiles.OrderBy(static profile => profile.PriorityOrder).ThenBy(static profile => profile.Name))
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

        if (await SaveProfilesAsync(Profiles.Append(profile).ToList(), cancellationToken))
        {
            Profiles.Add(profile);
            SelectedProfile = profile;
            NewProfileName = string.Empty;
        }
    }

    private async Task DeleteProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var selected = SelectedProfile;
        if (_dialogService.Confirm("Delete profile", $"Delete '{selected.Name}'? Its automation rules will be kept.")
            && await SaveProfilesAsync(Profiles.Where(profile => profile != selected).ToList(), cancellationToken))
        {
            Profiles.Remove(selected);
            SelectedProfile = Profiles.FirstOrDefault();
        }
    }

    private async Task ActivateProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is not { Enabled: true } selected)
        {
            _dialogService.ShowWarning("Profile is disabled", "Enable the profile before activating it.");
            return;
        }

        foreach (var profile in Profiles)
        {
            profile.IsActive = profile.Id == selected.Id;
            profile.TemporaryOverrideUntilUtc = null;
        }

        await SaveAndRefreshAsync(cancellationToken);
    }

    private async Task ActivateTemporaryAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is not { Enabled: true } selected)
        {
            _dialogService.ShowWarning("Profile is disabled", "Enable the profile before activating it.");
            return;
        }

        var until = DateTimeOffset.UtcNow.AddMinutes(TemporaryOverrideMinutes);
        foreach (var profile in Profiles)
        {
            profile.TemporaryOverrideUntilUtc = profile.Id == selected.Id ? until : null;
        }

        await SaveAndRefreshAsync(cancellationToken);
    }

    private async Task ClearActiveProfilesAsync(CancellationToken cancellationToken)
    {
        foreach (var profile in Profiles)
        {
            profile.IsActive = false;
            profile.TemporaryOverrideUntilUtc = null;
        }

        await SaveAndRefreshAsync(cancellationToken);
    }

    private Task SaveAsync(CancellationToken cancellationToken) => SaveAndRefreshAsync(cancellationToken);

    private async Task SaveAndRefreshAsync(CancellationToken cancellationToken)
    {
        if (await SaveProfilesAsync(Profiles.ToList(), cancellationToken))
        {
            await LoadAsync(cancellationToken);
        }
    }

    private async Task<bool> SaveProfilesAsync(List<AutomationProfile> profiles, CancellationToken cancellationToken)
    {
        IsSaving = true;
        try
        {
            SaveStatus = "Saving profiles...";
            var snapshot = profiles.Select(profile => new AutomationProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                Enabled = profile.Enabled,
                IsActive = profile.IsActive,
                AutoActivateProcessName = profile.AutoActivateProcessName,
                PriorityOrder = profile.PriorityOrder,
                TemporaryOverrideUntilUtc = profile.TemporaryOverrideUntilUtc,
                RuleIds = [.. profile.RuleIds]
            }).ToList();
            await _rulePersistenceService.UpdateAsync(store => store.Profiles = snapshot, cancellationToken);
            await _automationEngine.ReloadRulesAsync(cancellationToken);
            SaveStatus = "Profiles saved";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            SaveStatus = "Could not save profiles";
            _dialogService.ShowWarning("Profiles", exception.Message);
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void RefreshRuleAssignments()
    {
        RuleAssignments.Clear();
        if (SelectedProfile is null)
        {
            return;
        }

        foreach (var rule in _rules.OrderBy(static rule => rule.Name))
        {
            RuleAssignments.Add(new ProfileRuleAssignmentViewModel(SelectedProfile, rule));
        }
    }
}
