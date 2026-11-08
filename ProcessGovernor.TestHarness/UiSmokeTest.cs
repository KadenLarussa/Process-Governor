using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProcessGovernor.Services;
using ProcessGovernor.ViewModels;

namespace ProcessGovernor.TestHarness;

internal static partial class Program
{
    private static Task SmokeTestUiAsync(string? screenshots)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                Exception? failure = null;
                app.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await ExercisePagesAsync(screenshots);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        app.Shutdown();
                        // This test owns the dispatcher frame rather than running Application.Run.
                        app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.ApplicationIdle);
                    }
                });
                Dispatcher.Run();
                if (failure is not null)
                {
                    completion.SetException(failure);
                }
                else
                {
                    completion.SetResult();
                }
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task ExercisePagesAsync(string? screenshots)
    {
        var root = CreateTestRoot();
        var bindingMessages = new StringWriter();
        using var bindingListener = new TextWriterTraceListener(bindingMessages);
        var bindingSource = PresentationTraceSources.DataBindingSource;
        var oldLevel = bindingSource.Switch.Level;
        bindingSource.Switch.Level = SourceLevels.Error;
        bindingSource.Listeners.Add(bindingListener);
        MainWindow? window = null;

        try
        {
            var dialogs = new SmokeDialogService();
            using var provider = BuildProvider(root, dialogs);
            var settings = provider.GetRequiredService<ISettingsService>();
            var monitor = provider.GetRequiredService<IProcessMonitorService>();
            var main = provider.GetRequiredService<MainViewModel>();
            await main.InitializeAsync(CancellationToken.None);

            window = new MainWindow(settings, monitor) { DataContext = main, Width = 1040, Height = 640 };
            window.Show();
            // Monitor real Windows processes, but never start the automation engine in this UI test.
            using var monitorLifetime = new CancellationTokenSource();
            await monitor.StartAsync(monitorLifetime.Token);
            try
            {
                var dashboard = provider.GetRequiredService<DashboardViewModel>();
                for (var attempt = 0; attempt < 40 && dashboard.Processes.Count == 0; attempt++)
                {
                    await Task.Delay(100);
                }
                Assert.True(dashboard.Processes.Count > 0, "The dashboard should receive Windows process rows.");
                dashboard.SearchText = "no-process-should-match-this-test";
                Assert.True(dashboard.ProcessesView.IsEmpty, "Search should filter out nonmatching processes.");
                dashboard.ClearSearchCommand.Execute(null);
                Assert.True(!dashboard.ProcessesView.IsEmpty, "Clearing search should restore the process list.");

                var automations = provider.GetRequiredService<AutomationsViewModel>();
                var profiles = provider.GetRequiredService<ProfilesViewModel>();
                foreach (var size in new[] { (Width: 1040, Height: 640), (Width: 1380, Height: 860) })
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    foreach (var page in main.NavigationItems)
                    {
                        main.SelectedNavigationItem = page;
                        await CapturePageAsync(window, screenshots, $"{size.Width}-{page.Title}");
                        if (page.ViewModel == automations)
                        {
                            automations.CreateRuleCommand.Execute(null);
                            automations.UseGameLaunchPresetCommand.Execute(null);
                            Assert.True(automations.IsCreatingRule && automations.NewDryRun, "A new preset should open in test mode.");
                            await CapturePageAsync(window, screenshots, $"{size.Width}-NewRule");
                            automations.BackToRulesCommand.Execute(null);
                        }
                    }
                }

                main.SelectedNavigationItem = main.NavigationItems.Single(page => page.ViewModel == automations);
                automations.CreateRuleCommand.Execute(null);
                automations.NewRuleName = "UI smoke test rule";
                automations.BrowseForAppCommand.Execute(null);
                await ExecuteUiCommandAsync(automations.AddRuleCommand);
                Assert.True(!automations.IsCreatingRule && automations.SelectedRule?.Name == "UI smoke test rule",
                    "Saving a new rule should return to the saved list and select the new rule.");

                main.SelectedNavigationItem = main.NavigationItems.Single(page => page.ViewModel == profiles);
                profiles.NewProfileName = "UI smoke test profile";
                await ExecuteUiCommandAsync(profiles.AddProfileCommand);
                var assignment = profiles.RuleAssignments.Single(rule => rule.RuleName == "UI smoke test rule");
                assignment.IsAssigned = true;
                await ExecuteUiCommandAsync(profiles.ActivateTemporaryCommand);
                Assert.True(profiles.SelectedProfile?.TemporaryOverrideUntilUtc > DateTimeOffset.UtcNow,
                    "Temporary activation should survive saving and reloading the profile.");
                Assert.True(profiles.RuleAssignments.Single(rule => rule.RuleName == assignment.RuleName).IsAssigned,
                    "Rule assignment should survive saving the profile.");
                var originalId = automations.SelectedRule?.Id;
                main.SelectedNavigationItem = main.NavigationItems.Single(page => page.ViewModel == automations);
                automations.EditRuleCommand.Execute(null);
                Assert.True(automations.IsCreatingRule && automations.NewProcessName == "smoke-test-only.exe",
                    "Editing a rule should load its existing trigger.");
                automations.NewRuleName = "Edited UI smoke test rule";
                await ExecuteUiCommandAsync(automations.AddRuleCommand);
                Assert.True(automations.SelectedRule?.Id == originalId, "Editing should preserve the rule ID.");
                main.SelectedNavigationItem = main.NavigationItems.Single(page => page.ViewModel == profiles);
                Assert.True(profiles.RuleAssignments.Single(rule => rule.RuleName == "Edited UI smoke test rule").IsAssigned,
                    "Editing a rule should preserve its profile assignment.");
            }
            finally
            {
                await monitor.StopAsync();
            }

            window.AllowExit();
            window.Close();
            window = null;
            Assert.True(dialogs.Messages.Count == 0, string.Join(Environment.NewLine, dialogs.Messages));
            bindingListener.Flush();
            Assert.True(string.IsNullOrWhiteSpace(bindingMessages.ToString()), bindingMessages.ToString());
        }
        finally
        {
            if (window is not null)
            {
                window.AllowExit();
                window.Close();
            }
            bindingSource.Listeners.Remove(bindingListener);
            bindingSource.Switch.Level = oldLevel;
            DeleteTestRoot(root);
        }
    }

    private static async Task CapturePageAsync(MainWindow window, string? screenshots, string name)
    {
        await Task.Delay(180);
        window.UpdateLayout();
        var host = (ContentControl)window.FindName("PageHost");
        Assert.True(host.ActualWidth >= 750 && host.ActualHeight >= 500, $"{name} has insufficient layout space.");
        if (string.IsNullOrWhiteSpace(screenshots))
        {
            return;
        }

        Directory.CreateDirectory(screenshots);
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(screenshots, $"{name}.png"));
        png.Save(stream);
    }

    private static async Task ExecuteUiCommandAsync(Infrastructure.AsyncRelayCommand command)
    {
        Assert.True(command.CanExecute(null), "The UI command should be enabled.");
        command.Execute(null);
        while (command.IsExecuting)
        {
            await Task.Delay(20);
        }
    }

    private sealed class SmokeDialogService : IDialogService
    {
        public List<string> Messages { get; } = [];

        public string? PickExecutable() => "smoke-test-only.exe";

        public bool Confirm(string title, string message)
        {
            Messages.Add($"{title}: {message}");
            return false;
        }

        public void ShowInformation(string title, string message) => Messages.Add($"{title}: {message}");

        public void ShowWarning(string title, string message) => Messages.Add($"{title}: {message}");

        public void ShowError(string title, string message) => Messages.Add($"{title}: {message}");
    }
}
