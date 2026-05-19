# Process Governor

Process Governor is a lightweight Windows desktop process management and automation utility for power users and gamers. It is built with C#, .NET 8, WPF, MVVM, local JSON persistence, and dependency injection throughout.

This is not a RAM optimizer, FPS booster, registry tweak pack, or telemetry client. The app favors reversible process actions, low idle overhead, honest metrics, and local-only automation.

## Current Phase

Phase 1 is implemented:

- Live process dashboard with search, sorting, virtualized grid rows, and summary cards.
- CPU and RAM metrics from low-overhead APIs.
- Disk and GPU show `Unavailable` until reliable low-overhead collectors are added.
- Kill process and end process tree, with critical-process warnings.
- Open file location and copy executable path.
- Set process priority: Idle, Below Normal, Normal, Above Normal, High.
- Automation rules with process-start triggers.
- Automation actions for set priority and notification.
- Delay, cooldown, dry-run mode, and revert-on-exit rollback tracking.
- Logs page with severity filtering, search, and JSON/CSV export.
- Settings page with refresh cadence, tray behavior, compact-mode flag, safe mode flag, notifications, and rollback preference.
- Tray icon with open and exit commands.
- JSON persistence under `%LOCALAPPDATA%\ProcessGovernor`.

Phase 2 and 3 are intentionally not faked. Models and service boundaries are ready for profiles, power plans, CPU affinity, suspend/resume, fullscreen/window triggers, GPU metrics, and plugin support.

Phase 2 branch work adds:

- Higher contrast dark theme resources for readable dropdowns, grids, buttons, and editable cells.
- A presets-first automation builder for game launch boosts, focused performance, and quiet background apps.
- A default `Focused Performance` PC optimization profile that stays dormant until activated.
- CPU affinity display and dashboard presets for all CPUs, first half, and second half.
- Suspend and resume process actions using native Windows process handles.
- Per-process and summary disk I/O rates from native process I/O counters when Windows grants access.
- Automation CPU and RAM threshold triggers.
- Automation CPU affinity actions with decimal or hex masks.
- Automation Windows power plan actions with rollback to the previous plan when rollback protection is enabled.
- Manual, temporary, and auto process-based profile activation.
- Profile-scoped rule evaluation when a profile is active.

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
- `AutomationEngine`
- `RuleEvaluationService`
- `RulePersistenceService`
- `PowerPlanService`
- `ProcessActionService`
- `NotificationService`
- `ElevationService`
- `LoggingService`

The app avoids WMI polling in Phase 1. CPU and memory summary metrics use small Win32 P/Invoke calls, while per-process data comes from `System.Diagnostics.Process`. Access denied and race conditions from short-lived processes are handled gracefully.

## Notes For Next Steps

Recommended remaining Phase 2 work:

1. Add custom affinity mask editor UI beyond the current presets.
2. Add a profile rule-assignment picker instead of editing rule IDs manually in JSON.
3. Add measured performance and latency profiles with before/after logs instead of placebo optimization claims.
4. Add startup registration wiring for the existing Start with Windows setting.
5. Add compact-mode visual density changes across all pages.

Recommended Phase 3 implementation order:

1. Add window title and fullscreen detection.
2. Add GPU metrics using a reliable source with explicit `Unavailable` fallback.
3. Add plugin contracts after the core automation model stabilizes.
