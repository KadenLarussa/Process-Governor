using System.Windows;
using System.Windows.Threading;
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
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
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
            WriteCrashLog("startup-crash.txt", ex);
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

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("runtime-crash.txt", e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show(
            "Process Governor encountered an unexpected error and must close. Details were saved to the local logs folder.",
            "Process Governor",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(-1);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog("runtime-crash.txt", exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("background-task-error.txt", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string fileName, Exception exception)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ProcessGovernor",
                "logs");
            Directory.CreateDirectory(root);
            File.AppendAllText(
                Path.Combine(root, fileName),
                $"TimestampUtc: {DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Crash logging is best-effort; startup should still surface the original exception.
        }
    }
}
