using System.Windows;
using ProcessGovernor.Core;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Services;
using ProcessGovernor.ViewModels;
using ProcessGovernor.Views;

namespace ProcessGovernor;

public partial class App : System.Windows.Application
{
    private AppServiceProvider? _provider;
    private CancellationTokenSource? _appCancellation;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _appCancellation = new CancellationTokenSource();
        try
        {
            _provider = ServiceConfiguration.BuildProvider();
            var diagnosticsViewModel = _provider.GetRequiredService<StartupDiagnosticsViewModel>();
            var diagnosticsWindow = _provider.GetRequiredService<StartupDiagnosticsWindow>();
            diagnosticsWindow.DataContext = diagnosticsViewModel;
            diagnosticsWindow.Show();

            var diagnosticsProgress = new Progress<Models.StartupCheckUpdate>(diagnosticsViewModel.Apply);
            var diagnosticsResult = await _provider
                .GetRequiredService<IStartupDiagnosticsService>()
                .RunAsync(diagnosticsProgress, _appCancellation.Token)
                .ConfigureAwait(true);

            if (!diagnosticsResult.Succeeded)
            {
                System.Windows.MessageBox.Show(
                    diagnosticsResult.Summary,
                    "Process Governor startup checks failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            var mainViewModel = _provider.GetRequiredService<MainViewModel>();
            var window = _provider.GetRequiredService<MainWindow>();
            window.DataContext = mainViewModel;
            MainWindow = window;
            _provider.GetRequiredService<INotificationService>().Attach(window);

            var automationEngine = _provider.GetRequiredService<IAutomationEngine>();
            var processMonitor = _provider.GetRequiredService<IProcessMonitorService>();
            await automationEngine.StartAsync(_appCancellation.Token).ConfigureAwait(true);
            await processMonitor.StartAsync(_appCancellation.Token).ConfigureAwait(true);

            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            diagnosticsWindow.Close();
        }
        catch (Exception ex)
        {
            WriteStartupCrashLog(ex);
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

    private static void WriteStartupCrashLog(Exception exception)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ProcessGovernor",
                "logs");
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "startup-crash.txt"),
                $"TimestampUtc: {DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}");
        }
        catch
        {
            // Crash logging is best-effort; startup should still surface the original exception.
        }
    }
}
