using System.Diagnostics;
using ProcessGovernor.Core;
using ProcessGovernor.Infrastructure;
using ProcessGovernor.Models;
using ProcessGovernor.Services;
using ProcessGovernor.ViewModels;

namespace ProcessGovernor.TestHarness;

internal static class Program
{
    private static async Task<int> Main()
    {
        var runner = new TestRunner();
        await runner.RunAsync("JSON persistence round-trips typed settings", JsonPersistenceRoundTripsAsync);
        await runner.RunAsync("Default automations and profiles are created", DefaultAutomationStoreCreatesPresetsAsync);
        await runner.RunAsync("Rule evaluation handles process, threshold, and window triggers", RuleEvaluationMatchesExpectedTriggers);
        await runner.RunAsync("Startup diagnostics completes without fatal failures", StartupDiagnosticsCompletesAsync);
        await runner.RunAsync("GPU metrics service never fakes or throws", GpuMetricsServiceIsSafe);
        await runner.RunAsync("Process actions work on a disposable child process", ProcessActionsWorkOnChildProcessAsync);

        runner.WriteSummary();
        return runner.FailedCount == 0 ? 0 : 1;
    }

    private static async Task JsonPersistenceRoundTripsAsync()
    {
        var root = CreateTestRoot();
        try
        {
            var store = new JsonFileStore();
            var path = Path.Combine(root, "settings.json");
            var expected = new AppSettings
            {
                RefreshIntervalMs = 1750,
                CompactMode = true,
                AccentColor = "#ABCDEF"
            };

            await store.SaveAsync(path, expected, CancellationToken.None);
            var actual = await store.LoadAsync(path, static () => new AppSettings(), CancellationToken.None);

            Assert.Equal(1750, actual.RefreshIntervalMs, "Refresh interval should survive JSON persistence.");
            Assert.True(actual.CompactMode, "Compact mode should survive JSON persistence.");
            Assert.Equal("#ABCDEF", actual.AccentColor, "Accent color should survive JSON persistence.");
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    private static async Task DefaultAutomationStoreCreatesPresetsAsync()
    {
        var root = CreateTestRoot();
        try
        {
            var service = new RulePersistenceService(new AppPaths(root), new JsonFileStore());
            var store = await service.LoadAsync(CancellationToken.None);
            var ruleIds = store.Rules.Select(static rule => rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var brokenReferences = store.Profiles.SelectMany(static profile => profile.RuleIds).Count(ruleId => !ruleIds.Contains(ruleId));

            Assert.True(store.Rules.Any(static rule => rule.Name.Contains("Gaming Mode", StringComparison.OrdinalIgnoreCase)), "Gaming preset rule should exist.");
            Assert.True(store.Rules.Any(static rule => rule.Name.Contains("Safe PC Boost", StringComparison.OrdinalIgnoreCase)), "Safe PC Boost preset rule should exist.");
            Assert.True(store.Profiles.Any(static profile => profile.Name.Equals("Gaming", StringComparison.OrdinalIgnoreCase)), "Gaming profile should exist.");
            Assert.True(store.Profiles.Any(static profile => profile.Name.Equals("Safe PC Boost", StringComparison.OrdinalIgnoreCase)), "Safe PC Boost profile should exist.");
            Assert.Equal(0, brokenReferences, "Profiles should not reference missing rules.");
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    private static Task RuleEvaluationMatchesExpectedTriggers()
    {
        var service = new RuleEvaluationService();
        var process = new ProcessSnapshot
        {
            ProcessId = 123,
            Name = "cs2.exe",
            CpuUsagePercent = 72,
            WorkingSetBytes = 900L * 1024 * 1024
        };

        var processRule = new AutomationRule
        {
            Trigger = new AutomationTrigger
            {
                Type = AutomationTriggerType.ProcessStarted,
                ProcessName = "cs2"
            }
        };
        var cpuRule = new AutomationRule
        {
            Trigger = new AutomationTrigger
            {
                Type = AutomationTriggerType.CpuThreshold,
                Threshold = 50
            }
        };
        var windowRule = new AutomationRule
        {
            Trigger = new AutomationTrigger
            {
                Type = AutomationTriggerType.WindowTitleDetected,
                WindowTitleContains = "Counter-Strike"
            }
        };
        var fullscreenRule = new AutomationRule
        {
            Trigger = new AutomationTrigger
            {
                Type = AutomationTriggerType.FullscreenDetected,
                ProcessName = "cs2.exe"
            }
        };
        var window = new WindowSnapshot
        {
            ProcessId = 123,
            ProcessName = "cs2.exe",
            Title = "Counter-Strike 2",
            IsFullscreen = true,
            WindowHandle = 42
        };

        Assert.True(service.IsMatch(processRule, process, AutomationTriggerType.ProcessStarted), "ProcessStarted should normalize .exe names.");
        Assert.True(service.IsMatch(cpuRule, process, AutomationTriggerType.CpuThreshold), "CPU threshold should match high CPU process.");
        Assert.True(service.IsWindowMatch(windowRule, window, AutomationTriggerType.WindowTitleDetected), "Window title trigger should match case-insensitive title fragments.");
        Assert.True(service.IsWindowMatch(fullscreenRule, window, AutomationTriggerType.FullscreenDetected), "Fullscreen trigger should match fullscreen foreground process.");
        return Task.CompletedTask;
    }

    private static async Task StartupDiagnosticsCompletesAsync()
    {
        var root = CreateTestRoot();
        try
        {
            using var provider = BuildProvider(root);
            var diagnostics = provider.GetRequiredService<IStartupDiagnosticsService>();
            var updates = new List<StartupCheckUpdate>();
            var result = await diagnostics.RunAsync(new Progress<StartupCheckUpdate>(updates.Add), CancellationToken.None);
            var details = string.Join(
                " | ",
                updates
                    .Where(static update => update.Status is StartupCheckStatus.Passed or StartupCheckStatus.Warning or StartupCheckStatus.Failed)
                    .Select(static update => $"{update.Id}:{update.Status}:{update.Detail}"));

            Assert.True(result.Succeeded, $"{result.Summary} {details}");
            Assert.True(updates.Any(static update => update.Id == "folders" && update.Status == StartupCheckStatus.Passed), "Folder check should pass.");
            Assert.True(updates.Any(static update => update.Id == "rules" && update.Status == StartupCheckStatus.Passed), "Automation store check should pass.");
            Assert.True(updates.Any(static update => update.Id == "snapshot" && update.Status == StartupCheckStatus.Passed), "Process snapshot check should pass.");
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    private static Task GpuMetricsServiceIsSafe()
    {
        using var service = new GpuMetricsService();
        var snapshot = service.CaptureSummary();

        Assert.True(!string.IsNullOrWhiteSpace(snapshot.Display), "GPU metric display should be explicit.");
        Assert.True(snapshot.IsAvailable || snapshot.Display == "Unavailable", "GPU service must either report measured data or Unavailable.");
        return Task.CompletedTask;
    }

    private static async Task ProcessActionsWorkOnChildProcessAsync()
    {
        var root = CreateTestRoot();
        Process? child = null;
        try
        {
            using var provider = BuildProvider(root);
            var settings = provider.GetRequiredService<ISettingsService>();
            var logging = provider.GetRequiredService<ILoggingService>();
            await settings.InitializeAsync(CancellationToken.None);
            await logging.InitializeAsync(CancellationToken.None);

            child = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping 127.0.0.1 -n 30",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            Assert.True(child is not null, "Child process should start.");

            var actions = provider.GetRequiredService<IProcessActionService>();
            var priorityResult = await actions.SetPriorityAsync(child!.Id, ProcessPriorityClass.BelowNormal, CancellationToken.None);
            Assert.True(priorityResult.Succeeded, priorityResult.Message);

            var priority = await actions.GetPriorityAsync(child.Id, CancellationToken.None);
            Assert.Equal(ProcessPriorityClass.BelowNormal, priority, "Child priority should be changed.");

            var invalidAffinity = await actions.SetCpuAffinityAsync(child.Id, 0, CancellationToken.None);
            Assert.True(!invalidAffinity.Succeeded, "Invalid CPU affinity must fail safely.");

            var killResult = await actions.KillAsync(child.Id, entireProcessTree: true, forceCriticalProcess: false, CancellationToken.None);
            Assert.True(killResult.Succeeded, killResult.Message);
            child = null;
        }
        finally
        {
            if (child is { HasExited: false })
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }

            child?.Dispose();
            DeleteTestRoot(root);
        }
    }

    private static AppServiceProvider BuildProvider(string root)
        => new ServiceRegistry()
            .AddSingleton(_ => new AppPaths(root))
            .AddSingleton<IJsonFileStore, JsonFileStore>()
            .AddSingleton<ISettingsService, SettingsService>()
            .AddSingleton<IStartupRegistrationService, StartupRegistrationService>()
            .AddSingleton<ILoggingService, LoggingService>()
            .AddSingleton<IGpuMetricsService, GpuMetricsService>()
            .AddSingleton<IProcessMonitorService, ProcessMonitorService>()
            .AddSingleton<IProcessActionService, ProcessActionService>()
            .AddSingleton<IRulePersistenceService, RulePersistenceService>()
            .AddSingleton<IRuleEvaluationService, RuleEvaluationService>()
            .AddSingleton<IAutomationEngine, AutomationEngine>()
            .AddSingleton<IPowerPlanService, PowerPlanService>()
            .AddSingleton<IWindowDetectionService, WindowDetectionService>()
            .AddSingleton<IStartupDiagnosticsService, StartupDiagnosticsService>()
            .AddSingleton<INotificationService, NotificationService>()
            .AddSingleton<IElevationService, ElevationService>()
            .AddSingleton<IDialogService, DialogService>()
            .AddSingleton<MainViewModel>()
            .AddSingleton<DashboardViewModel>()
            .AddSingleton<AutomationsViewModel>()
            .AddSingleton<ProfilesViewModel>()
            .AddSingleton<LogsViewModel>()
            .AddSingleton<SettingsViewModel>()
            .Build();

    private static string CreateTestRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTestRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

internal sealed class TestRunner
{
    private int _passedCount;

    public int FailedCount { get; private set; }

    public async Task RunAsync(string name, Func<Task> test)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await test();
            stopwatch.Stop();
            _passedCount++;
            Console.WriteLine($"PASS {name} ({stopwatch.Elapsed.TotalMilliseconds:0} ms)");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            FailedCount++;
            Console.WriteLine($"FAIL {name} ({stopwatch.Elapsed.TotalMilliseconds:0} ms)");
            Console.WriteLine($"     {ex.Message}");
        }
    }

    public void WriteSummary()
    {
        Console.WriteLine();
        Console.WriteLine($"Summary: {_passedCount} passed, {FailedCount} failed.");
    }
}

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected '{expected}', actual '{actual}'.");
        }
    }
}
