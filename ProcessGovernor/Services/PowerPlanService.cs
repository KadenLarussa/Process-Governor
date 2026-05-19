using System.Diagnostics;
using System.Text.RegularExpressions;
using ProcessGovernor.Models;

namespace ProcessGovernor.Services;

public sealed partial class PowerPlanService : IPowerPlanService
{
    private readonly ILoggingService _loggingService;

    public PowerPlanService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public async Task<string?> GetActivePlanAsync(CancellationToken cancellationToken)
    {
        var result = await RunPowerCfgAsync("/GETACTIVESCHEME", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return null;
        }

        var match = ActivePlanRegex().Match(result.Message);
        return match.Success ? match.Groups["name"].Value.Trim() : result.Message.Trim();
    }

    public async Task<ProcessActionResult> SetActivePlanByNameAsync(string planName, CancellationToken cancellationToken)
    {
        var listResult = await RunPowerCfgAsync("/LIST", cancellationToken).ConfigureAwait(false);
        if (!listResult.Succeeded)
        {
            return listResult;
        }

        foreach (Match match in PowerPlanListRegex().Matches(listResult.Message))
        {
            var id = match.Groups["id"].Value;
            var name = match.Groups["name"].Value.Trim();
            if (!name.Equals(planName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var activateResult = await RunPowerCfgAsync($"/SETACTIVE {id}", cancellationToken).ConfigureAwait(false);
            if (activateResult.Succeeded)
            {
                await _loggingService.LogAsync(LogSeverity.Information, nameof(PowerPlanService), $"Activated power plan '{name}'.", cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return activateResult;
        }

        return ProcessActionResult.Failure($"Power plan '{planName}' was not found.");
    }

    private static async Task<ProcessActionResult> RunPowerCfgAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return ProcessActionResult.Failure("Unable to start powercfg.exe.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            return process.ExitCode == 0
                ? ProcessActionResult.Success(output)
                : ProcessActionResult.Failure(string.IsNullOrWhiteSpace(error) ? output : error);
        }
        catch (Exception ex)
        {
            return ProcessActionResult.Failure($"powercfg.exe failed: {ex.Message}", ex);
        }
    }

    [GeneratedRegex(@"Power Scheme GUID:\s*(?<id>[a-fA-F0-9-]+)\s*\((?<name>.+?)\)", RegexOptions.Compiled)]
    private static partial Regex ActivePlanRegex();

    [GeneratedRegex(@"Power Scheme GUID:\s*(?<id>[a-fA-F0-9-]+)\s*\((?<name>.+?)\)", RegexOptions.Compiled)]
    private static partial Regex PowerPlanListRegex();
}
