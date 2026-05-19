using ProcessGovernor.Core;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.ViewModels;

namespace ProcessGovernor.Services;

public static class ServiceConfiguration
{
    public static AppServiceProvider BuildProvider()
    {
        return new ServiceRegistry()
            .AddSingleton<AppPaths>()
            .AddSingleton<IJsonFileStore, JsonFileStore>()
            .AddSingleton<ISettingsService, SettingsService>()
            .AddSingleton<ILoggingService, LoggingService>()
            .AddSingleton<IProcessMonitorService, ProcessMonitorService>()
            .AddSingleton<IProcessActionService, ProcessActionService>()
            .AddSingleton<IRulePersistenceService, RulePersistenceService>()
            .AddSingleton<IRuleEvaluationService, RuleEvaluationService>()
            .AddSingleton<IAutomationEngine, AutomationEngine>()
            .AddSingleton<IPowerPlanService, PowerPlanService>()
            .AddSingleton<INotificationService, NotificationService>()
            .AddSingleton<IElevationService, ElevationService>()
            .AddSingleton<IDialogService, DialogService>()
            .AddSingleton<MainViewModel>()
            .AddSingleton<DashboardViewModel>()
            .AddSingleton<AutomationsViewModel>()
            .AddSingleton<ProfilesViewModel>()
            .AddSingleton<LogsViewModel>()
            .AddSingleton<SettingsViewModel>()
            .AddSingleton<MainWindow>()
            .Build();
    }
}
