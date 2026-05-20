# Process Governor

Process Governor is a lightweight Windows desktop process management and automation utility for power users and gamers. It is built with C#, .NET 8, WPF, MVVM, local JSON persistence, and dependency injection throughout.

This is not a RAM optimizer, FPS booster, registry tweak pack, or telemetry client. The app favors reversible process actions, low idle overhead, honest metrics, and local-only automation.

## Current Phase

The current branch includes:

- Live process dashboard with search, sorting, virtualized grid rows, and summary cards.
- CPU and RAM metrics from low-overhead APIs.
- Disk I/O rates from native process I/O counters when Windows grants access.
- Summary GPU usage from the Windows PDH GPU Engine counter when available; otherwise `Unavailable`.
- Per-process GPU usage remains `Unavailable` instead of being guessed.
- Kill process and end process tree, with critical-process warnings.
- Open file location and copy executable path.
- Set process priority: Idle, Below Normal, Normal, Above Normal, High.
- Automation rules with process-start triggers.
- Automation actions for set priority and notification.
- Delay, cooldown, dry-run mode, and revert-on-exit rollback tracking.
- Logs page with severity filtering, search, and JSON/CSV export.
- Settings page with refresh cadence, tray behavior, compact-mode density, safe mode flag, notifications, startup registration, and rollback preference.
- Tray icon with open and exit commands.
- JSON persistence under `%LOCALAPPDATA%\ProcessGovernor`.

Phase 2 and 3 are intentionally not faked. Performance changes are reversible where possible, metrics are marked `Unavailable` when Windows cannot provide them reliably, and automation logs include measured before-state details.

Phase 2 branch work adds:

- Higher contrast dark theme resources for readable dropdowns, grids, buttons, editable cells, checkboxes, tooltips, scrollbars, and native dark title bars.
- A presets-first automation builder with beginner-friendly labels for game launch boosts, safe PC boosts, and quiet background apps.
- A default `Safe PC Boost` PC optimization profile that stays dormant until activated.
- CPU affinity display and dashboard presets for all CPUs, first half, and second half.
- Suspend and resume process actions using native Windows process handles.
- Per-process and summary disk I/O rates from native process I/O counters when Windows grants access.
- Automation CPU and RAM threshold triggers.
- Automation CPU affinity actions with decimal or hex masks.
- Automation Windows power plan actions with rollback to the previous plan when rollback protection is enabled.
- Manual, temporary, and auto process-based profile activation.
- Profile-scoped rule evaluation when a profile is active.

Phase 3 branch work adds:

- Window title and fullscreen automation triggers using low-overhead foreground-window Win32 APIs.
- Button-like automation toggles and clearer saved-rule state pills instead of checkbox-heavy rule editing.
- A profile rule-assignment picker, so users do not edit rule IDs by hand.
- A custom CPU affinity mask editor with all, first-half, and second-half presets.
- Start with Windows registration through the current user's Run key.
- Compact-mode shell density changes across pages.
- A low-overhead GPU metrics service that uses PDH when available and falls back to `Unavailable`.
- Plugin contracts for future extension without runtime plugin loading yet.

The optimization profile is intentionally conservative. It does not clean RAM, force garbage collection, apply registry tweak packs, disable services blindly, or claim fake FPS gains. It uses measurable, reversible actions: priority changes, power plan switching, affinity options, notifications, logs, and rollback where possible.

## Requirements

- Windows 10 or newer
- .NET 8 Windows Desktop Runtime
- .NET SDK capable of building `net8.0-windows` if you want to compile from source

## Build

From the repository root:

```powershell
dotnet build ProcessGovernor.sln
```

Run from source:

```powershell
dotnet run --project .\ProcessGovernor\ProcessGovernor.csproj
```

Run the local functionality harness:

```powershell
dotnet run --project .\ProcessGovernor.TestHarness\ProcessGovernor.TestHarness.csproj
```

Publish a self-contained folder build:

```powershell
dotnet publish .\ProcessGovernor\ProcessGovernor.csproj -c Release -r win-x64 --self-contained true -o .\publish\ProcessGovernor
```

## Local Data

Process Governor stores human-readable JSON files here:

```text
%LOCALAPPDATA%\ProcessGovernor\config\settings.json
%LOCALAPPDATA%\ProcessGovernor\config\automations.json
%LOCALAPPDATA%\ProcessGovernor\logs\events.json
```

## Architecture

Important services:

- `ProcessMonitorService`
- `GpuMetricsService`
- `WindowDetectionService`
- `AutomationEngine`
- `RuleEvaluationService`
- `RulePersistenceService`
- `PowerPlanService`
- `ProcessActionService`
- `NotificationService`
- `ElevationService`
- `LoggingService`
- `StartupRegistrationService`
- `StartupDiagnosticsService`

The app avoids WMI polling for core process monitoring. CPU and memory summary metrics use small Win32 P/Invoke calls, summary GPU uses PDH when the Windows counter is available, and per-process data comes from `System.Diagnostics.Process`. Access denied and race conditions from short-lived processes are handled gracefully.

The startup diagnostics window runs local readiness checks before the main UI opens. See [docs/startup-diagnostics-test-plan.md](docs/startup-diagnostics-test-plan.md) for the analysis and smoke-test plan.
