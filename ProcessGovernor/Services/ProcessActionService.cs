using System.Diagnostics;
using System.Windows;
using ProcessGovernor.Infrastructure;
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
                return await LogFailureAsync($"{processName} is a protected or critical Windows process.", null, processId, cancellationToken).ConfigureAwait(false);
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
            return await LogFailureAsync("The process no longer exists.", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await LogFailureAsync("Access denied. Try running Process Governor as administrator.", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return await LogFailureAsync($"Windows refused the operation: {ex.Message}", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return await LogFailureAsync("The process exited before the action completed.", ex, processId, cancellationToken).ConfigureAwait(false);
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
            return await LogFailureAsync("The process no longer exists.", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await LogFailureAsync("Access denied. Try running Process Governor as administrator.", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return await LogFailureAsync($"Windows refused the operation: {ex.Message}", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return await LogFailureAsync("The process exited before the action completed.", ex, processId, cancellationToken).ConfigureAwait(false);
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

    public async Task<ProcessActionResult> SetCpuAffinityAsync(int processId, long affinityMask, CancellationToken cancellationToken)
    {
        if (affinityMask <= 0)
        {
            return await LogFailureAsync("CPU affinity mask must include at least one CPU.", null, processId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var oldAffinity = process.ProcessorAffinity.ToInt64();
            process.ProcessorAffinity = new IntPtr(affinityMask);
            var message = $"Changed CPU affinity for {process.ProcessName} ({processId}) from 0x{oldAffinity:X} to 0x{affinityMask:X}.";
            await _loggingService.LogAsync(LogSeverity.Information, nameof(ProcessActionService), message, processId: processId, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProcessActionResult.Success(message);
        }
        catch (ArgumentException ex)
        {
            return await LogFailureAsync("The process no longer exists.", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await LogFailureAsync("Access denied. Try running Process Governor as administrator.", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return await LogFailureAsync($"Windows refused the operation: {ex.Message}", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return await LogFailureAsync("The process exited before the action completed.", ex, processId, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<long?> GetCpuAffinityAsync(int processId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(processId);
            return Task.FromResult<long?>(process.ProcessorAffinity.ToInt64());
        }
        catch
        {
            return Task.FromResult<long?>(null);
        }
    }

    public async Task<ProcessActionResult> SuspendAsync(int processId, bool forceCriticalProcess, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!forceCriticalProcess && IsCriticalProcessName(process.ProcessName))
            {
                return await LogFailureAsync($"{process.ProcessName} is a protected or critical Windows process.", null, processId, cancellationToken).ConfigureAwait(false);
            }

            var handle = Core.NativeMethods.OpenProcess(Core.NativeMethods.ProcessSuspendResume, false, processId);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return await LogFailureAsync("Unable to open the process for suspend. Try running as administrator.", null, processId, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var status = Core.NativeMethods.NtSuspendProcess(handle);
                if (status != 0)
                {
                    return await LogFailureAsync($"Windows refused suspend with NTSTATUS 0x{status:X8}.", null, processId, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Core.NativeMethods.CloseHandle(handle);
            }

            var message = $"Suspended {process.ProcessName} ({processId}).";
            await _loggingService.LogAsync(LogSeverity.Warning, nameof(ProcessActionService), message, processId: processId, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProcessActionResult.Success(message);
        }
        catch (ArgumentException ex)
        {
            return await LogFailureAsync("The process no longer exists.", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return await LogFailureAsync($"Suspend failed: {ex.Message}", ex, processId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ProcessActionResult> ResumeAsync(int processId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(processId);
            var handle = Core.NativeMethods.OpenProcess(Core.NativeMethods.ProcessSuspendResume, false, processId);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return await LogFailureAsync("Unable to open the process for resume. Try running as administrator.", null, processId, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var status = Core.NativeMethods.NtResumeProcess(handle);
                if (status != 0)
                {
                    return await LogFailureAsync($"Windows refused resume with NTSTATUS 0x{status:X8}.", null, processId, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Core.NativeMethods.CloseHandle(handle);
            }

            var message = $"Resumed {process.ProcessName} ({processId}).";
            await _loggingService.LogAsync(LogSeverity.Information, nameof(ProcessActionService), message, processId: processId, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ProcessActionResult.Success(message);
        }
        catch (ArgumentException ex)
        {
            return await LogFailureAsync("The process no longer exists.", ex, processId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return await LogFailureAsync($"Resume failed: {ex.Message}", ex, processId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ProcessActionResult> OpenFileLocationAsync(string? executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return await LogFailureAsync("Executable path is unavailable.", null, null, cancellationToken).ConfigureAwait(false);
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
            return await LogFailureAsync($"Unable to open file location: {ex.Message}", ex, null, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ProcessActionResult> CopyPathAsync(string? executablePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return await LogFailureAsync("Executable path is unavailable.", null, null, cancellationToken).ConfigureAwait(false);
        }

        await UiDispatch.InvokeAsync(() => System.Windows.Clipboard.SetText(executablePath));
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

    private async Task<ProcessActionResult> LogFailureAsync(string message, Exception? exception, int? processId, CancellationToken cancellationToken)
    {
        await _loggingService.LogAsync(
            LogSeverity.Warning,
            nameof(ProcessActionService),
            message,
            exception?.Message,
            processId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return ProcessActionResult.Failure(message, exception);
    }
}
