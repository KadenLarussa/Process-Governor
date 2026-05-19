using System.Diagnostics;
using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class RulePersistenceService : IRulePersistenceService
{
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

        if (!exists)
        {
            await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }

        return file;
    }

    public Task SaveAsync(AutomationStoreFile store, CancellationToken cancellationToken)
        => _store.SaveAsync(_paths.GetConfigPath("automations.json"), store, cancellationToken);

    private static AutomationStoreFile CreateDefaultStore()
    {
        var cs2Rule = new AutomationRule
        {
            Name = "CS2 priority notification",
            Enabled = false,
            Trigger = new AutomationTrigger
            {
                Type = AutomationTriggerType.ProcessStarted,
                ProcessName = "cs2.exe"
            },
            RevertOnExit = true,
            CooldownSeconds = 30,
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
                    Type = AutomationActionType.SendNotification,
                    NotificationTitle = "Gaming rule fired",
                    NotificationMessage = "cs2.exe detected. Priority was set to High."
                }
            ]
        };

        return new AutomationStoreFile
        {
            Rules = [cs2Rule],
            Profiles =
            [
                new AutomationProfile
                {
                    Name = "Gaming",
                    PriorityOrder = 10,
                    AutoActivateProcessName = "cs2.exe",
                    RuleIds = [cs2Rule.Id]
                },
                new AutomationProfile
                {
                    Name = "Work",
                    PriorityOrder = 20
                },
                new AutomationProfile
                {
                    Name = "Quiet Mode",
                    PriorityOrder = 30
                }
            ]
        };
    }
}
