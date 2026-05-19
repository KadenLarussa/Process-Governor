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

Recommended Phase 2 implementation order:

1. Add CPU affinity UI and action support.
2. Add manual/automatic profile activation semantics.
3. Add power plan switching to automation actions using `PowerPlanService`.
4. Add suspend/resume only after choosing a safe Windows API strategy.
5. Add better disk metrics using PDH or ETW after measuring overhead.
6. Add measured performance and latency profiles with before/after logs instead of placebo optimization claims.

Recommended Phase 3 implementation order:

1. Add window title and fullscreen detection.
2. Add GPU metrics using a reliable source with explicit `Unavailable` fallback.
3. Add plugin contracts after the core automation model stabilizes.
