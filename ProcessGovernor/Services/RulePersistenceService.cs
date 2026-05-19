using System.Diagnostics;
using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class RulePersistenceService : IRulePersistenceService
{
    private const string SafePcBoostRuleName = "Safe PC Boost: busy app helper";
    private const string LegacyFocusedRuleName = "Focused Performance: high-load app boost";
    private readonly AppPaths _paths;
    private readonly IJsonFileStore _store;

    public RulePersistenceService(AppPaths paths, IJsonFileStore store)
    {
        _paths = paths;
        _store = store;
    }

    public async Task<AutomationStoreFile> LoadAsync(CancellationToken cancellationToken)
    {
        var path = _paths.GetConfigPath("automations.json");
        var exists = File.Exists(path);
        var file = await _store.LoadAsync(path, CreateDefaultStore, cancellationToken).ConfigureAwait(false);
        var changed = EnsureDefaultPresets(file);
        changed |= RemoveRetiredDefaultRules(file);

        if (!exists || changed)
        {
            await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }

        return file;
    }

    public Task SaveAsync(AutomationStoreFile store, CancellationToken cancellationToken)
        => _store.SaveAsync(_paths.GetConfigPath("automations.json"), store, cancellationToken);

    private static AutomationStoreFile CreateDefaultStore()
    {
        var store = new AutomationStoreFile();
        EnsureDefaultPresets(store);
        return store;
    }

    private static bool EnsureDefaultPresets(AutomationStoreFile store)
    {
        var changed = false;

        var cs2Rule = EnsureRule(store, "Gaming Mode: CS2 launch boost", () => new AutomationRule
        {
            Name = "Gaming Mode: CS2 launch boost",
            Enabled = true,
            Trigger = new AutomationTrigger
            {
                Type = AutomationTriggerType.ProcessStarted,
                ProcessName = "cs2.exe"
            },
            RevertOnExit = true,
            CooldownSeconds = 30,
            DelaySeconds = 1,
            Actions =
            [
                new AutomationAction
                {
                    Type = AutomationActionType.SetProcessPriority,
                    TargetProcessName = "cs2.exe",
                    Priority = ProcessPriorityClass.High
                },
                new AutomationAction
                {
                    Type = AutomationActionType.ChangePowerPlan,
                    PowerPlanName = "High performance"
                },
                new AutomationAction
                {
                    Type = AutomationActionType.SendNotification,
                    NotificationTitle = "Gaming mode",
                    NotificationMessage = "cs2.exe detected. Priority and power plan actions were applied."
                }
            ]
        }, ref changed);

        RenameLegacyRule(store, LegacyFocusedRuleName, SafePcBoostRuleName, ref changed);

        _ = EnsureRule(store, SafePcBoostRuleName, () => new AutomationRule
        {
            Name = SafePcBoostRuleName,
            Enabled = true,
            Trigger = new AutomationTrigger
            {
                Type = AutomationTriggerType.CpuThreshold,
                Threshold = 55
            },
            RevertOnExit = true,
            CooldownSeconds = 180,
            DelaySeconds = 0,
            Actions =
            [
                new AutomationAction
                {
                    Type = AutomationActionType.SetProcessPriority,
                    Priority = ProcessPriorityClass.AboveNormal
                },
                new AutomationAction
                {
                    Type = AutomationActionType.ChangePowerPlan,
                    PowerPlanName = "High performance"
                },
                new AutomationAction
                {
                    Type = AutomationActionType.SendNotification,
                    NotificationTitle = "Safe PC Boost",
                    NotificationMessage = "A high-load process was detected and given a temporary priority boost."
                }
            ]
        }, ref changed);

        var focusedRule = store.Rules.First(static rule => rule.Name.Equals(SafePcBoostRuleName, StringComparison.OrdinalIgnoreCase));
        EnsureProfile(store, "Safe PC Boost", 5, null, [focusedRule.Id], ref changed);
        EnsureProfile(store, "Gaming", 10, "cs2.exe", [cs2Rule.Id], ref changed);
        EnsureProfile(store, "Work", 20, null, [], ref changed);
        EnsureProfile(store, "Quiet Mode", 30, null, [], ref changed);

        return changed;
    }

    private static bool RemoveRetiredDefaultRules(AutomationStoreFile store)
    {
        var retiredRules = store.Rules
            .Where(static rule => !rule.Enabled && rule.Name.Equals("CS2 priority notification", StringComparison.OrdinalIgnoreCase))
            .Select(static rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (retiredRules.Count == 0)
        {
            return false;
        }

        store.Rules.RemoveAll(rule => retiredRules.Contains(rule.Id));
        foreach (var profile in store.Profiles)
        {
            profile.RuleIds.RemoveAll(ruleId => retiredRules.Contains(ruleId));
        }

        return true;
    }

    private static AutomationRule EnsureRule(AutomationStoreFile store, string name, Func<AutomationRule> factory, ref bool changed)
    {
        var existing = store.Rules.FirstOrDefault(rule => rule.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var rule = factory();
        store.Rules.Add(rule);
        changed = true;
        return rule;
    }

    private static void RenameLegacyRule(AutomationStoreFile store, string oldName, string newName, ref bool changed)
    {
        if (store.Rules.Any(rule => rule.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var legacyRule = store.Rules.FirstOrDefault(rule => rule.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (legacyRule is null)
        {
            return;
        }

        legacyRule.Name = newName;
        changed = true;
    }

    private static AutomationProfile EnsureProfile(
        AutomationStoreFile store,
        string name,
        int priorityOrder,
        string? autoActivateProcessName,
        IReadOnlyList<string> ruleIds,
        ref bool changed)
    {
        var profile = store.Profiles.FirstOrDefault(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            profile = new AutomationProfile
            {
                Name = name,
                PriorityOrder = priorityOrder,
                AutoActivateProcessName = autoActivateProcessName
            };
            store.Profiles.Add(profile);
            changed = true;
        }

        foreach (var ruleId in ruleIds)
        {
            if (!profile.RuleIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase))
            {
                profile.RuleIds.Add(ruleId);
                changed = true;
            }
        }

        return profile;
    }

}
