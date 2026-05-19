using System.Diagnostics;
using System.Windows;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class ProcessActionService : IProcessActionService
{
    private readonly ILoggingService _loggingService;

    public ProcessActionService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public bool IsCriticalProcess(ProcessSnapshot process) => process.IsCritical;

    public async Task<ProcessActionResult> KillAsync(int processId, bool entireProcessTree, bool forceCriticalProcess, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var processName = process.ProcessName;
            if (!forceCriticalProcess && IsCriticalProcessName(processName))
            {
                return ProcessActionResult.Failure($"{processName} is a protected or critical Windows process.");
            }

            process.Kill(entireProcessTree);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var message = entireProcessTree
                ? $"Ended process tree for {processName} ({processId})."
                : $"Killed {processName} ({processId}).";
            await _loggingService.LogAsync(LogSeverity.Warning, nameof(ProcessActionService), message, processId: processId, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProcessActionResult.Success(message);
        }
        catch (ArgumentException ex)
        {
            return ProcessActionResult.Failure("The process no longer exists.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ProcessActionResult.Failure("Access denied. Try running Process Governor as administrator.", ex);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return ProcessActionResult.Failure($"Windows refused the operation: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            return ProcessActionResult.Failure("The process exited before the action completed.", ex);
        }
    }

    public async Task<ProcessActionResult> SetPriorityAsync(int processId, ProcessPriorityClass priority, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var oldPriority = process.PriorityClass;
            process.PriorityClass = priority;
            var message = $"Changed priority for {process.ProcessName} ({processId}) from {oldPriority} to {priority}.";
            await _loggingService.LogAsync(LogSeverity.Information, nameof(ProcessActionService), message, processId: processId, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProcessActionResult.Success(message);
        }
        catch (ArgumentException ex)
        {
            return ProcessActionResult.Failure("The process no longer exists.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ProcessActionResult.Failure("Access denied. Try running Process Governor as administrator.", ex);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return ProcessActionResult.Failure($"Windows refused the operation: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            return ProcessActionResult.Failure("The process exited before the action completed.", ex);
        }
    }

    public Task<ProcessPriorityClass?> GetPriorityAsync(int processId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(processId);
            return Task.FromResult<ProcessPriorityClass?>(process.PriorityClass);
        }
        catch
        {
            return Task.FromResult<ProcessPriorityClass?>(null);
        }
    }

    public async Task<ProcessActionResult> OpenFileLocationAsync(string? executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return ProcessActionResult.Failure("Executable path is unavailable.");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{executablePath}\"",
                UseShellExecute = true
            });

            await _loggingService.LogAsync(LogSeverity.Information, nameof(ProcessActionService), $"Opened file location for {executablePath}.", cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProcessActionResult.Success("Opened file location.");
        }
        catch (Exception ex)
        {
            return ProcessActionResult.Failure($"Unable to open file location: {ex.Message}", ex);
        }
    }

    public async Task<ProcessActionResult> CopyPathAsync(string? executablePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return ProcessActionResult.Failure("Executable path is unavailable.");
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => System.Windows.Clipboard.SetText(executablePath));
        await _loggingService.LogAsync(LogSeverity.Information, nameof(ProcessActionService), $"Copied path: {executablePath}.", cancellationToken: cancellationToken).ConfigureAwait(false);
        return ProcessActionResult.Success("Copied path.");
    }

    private static bool IsCriticalProcessName(string processName)
    {
        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        return normalized.Equals("Idle", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("System", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Registry", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("smss", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("csrss", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("wininit", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("winlogon", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("services", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("lsass", StringComparison.OrdinalIgnoreCase);
    }
}
