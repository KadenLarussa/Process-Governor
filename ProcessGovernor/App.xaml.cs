using System.Windows;
using ProcessGovernor.Core;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Services;
using ProcessGovernor.ViewModels;

namespace ProcessGovernor;

public partial class App : System.Windows.Application
{
    private AppServiceProvider? _provider;
    private CancellationTokenSource? _appCancellation;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _appCancellation = new CancellationTokenSource();
        try
        {
            _provider = ServiceConfiguration.BuildProvider();
            _provider.GetRequiredService<AppPaths>().EnsureCreated();

            var settingsService = _provider.GetRequiredService<ISettingsService>();
            var loggingService = _provider.GetRequiredService<ILoggingService>();
            var automationEngine = _provider.GetRequiredService<IAutomationEngine>();
            var processMonitor = _provider.GetRequiredService<IProcessMonitorService>();

            await settingsService.InitializeAsync(_appCancellation.Token).ConfigureAwait(true);
            await loggingService.InitializeAsync(_appCancellation.Token).ConfigureAwait(true);
            await automationEngine.InitializeAsync(_appCancellation.Token).ConfigureAwait(true);

            var mainViewModel = _provider.GetRequiredService<MainViewModel>();
            await mainViewModel.InitializeAsync(_appCancellation.Token).ConfigureAwait(true);

            var window = _provider.GetRequiredService<MainWindow>();
            window.DataContext = mainViewModel;
            _provider.GetRequiredService<INotificationService>().Attach(window);

            await automationEngine.StartAsync(_appCancellation.Token).ConfigureAwait(true);
            await processMonitor.StartAsync(_appCancellation.Token).ConfigureAwait(true);

            window.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.ToString(), "Process Governor failed to start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_provider is not null)
        {
            if (_appCancellation is not null)
            {
                await _appCancellation.CancelAsync().ConfigureAwait(false);
            }

            await _provider.GetRequiredService<IAutomationEngine>().StopAsync().ConfigureAwait(false);
            await _provider.GetRequiredService<IProcessMonitorService>().StopAsync().ConfigureAwait(false);
            _provider.Dispose();
        }

        _appCancellation?.Dispose();
        base.OnExit(e);
    }
}
