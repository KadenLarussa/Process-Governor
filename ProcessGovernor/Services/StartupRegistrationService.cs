using Microsoft.Win32;
using ProcessGovernor.Core;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public Task<ProcessActionResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (key is null)
                {
                    return ProcessActionResult.Failure("Windows startup registration key is unavailable.");
                }

                if (!enabled)
                {
                    key.DeleteValue(AppConstants.AppName, throwOnMissingValue: false);
                    return ProcessActionResult.Success("Start with Windows disabled.");
                }

                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    return ProcessActionResult.Failure("Unable to resolve the Process Governor executable path.");
                }

                key.SetValue(AppConstants.AppName, Quote(executablePath), RegistryValueKind.String);
                return ProcessActionResult.Success("Start with Windows enabled.");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                return ProcessActionResult.Failure($"Windows refused startup registration: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static string Quote(string value) => $"\"{value}\"";
}
