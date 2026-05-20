using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;

namespace ProcessGovernor.ViewModels;

public sealed class ProfileRuleAssignmentViewModel : ObservableObject
{
    private readonly AutomationProfile _profile;
    private readonly AutomationRule _rule;

    public ProfileRuleAssignmentViewModel(AutomationProfile profile, AutomationRule rule)
    {
        _profile = profile;
        _rule = rule;
    }

    public string RuleName => _rule.Name;

    public string Trigger => AutomationRuleViewModel.GetTriggerSummary(_rule);

    public string Actions => AutomationRuleViewModel.GetActionSummary(_rule);

    public bool IsAssigned
    {
        get => _profile.RuleIds.Contains(_rule.Id, StringComparer.OrdinalIgnoreCase);
        set
        {
            if (value == IsAssigned)
            {
                return;
            }

            if (value)
            {
                _profile.RuleIds.Add(_rule.Id);
            }
            else
            {
                _profile.RuleIds.RemoveAll(ruleId => ruleId.Equals(_rule.Id, StringComparison.OrdinalIgnoreCase));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(AssignedDisplay));
        }
    }

    public string AssignedDisplay => IsAssigned ? "In" : "Out";
}
