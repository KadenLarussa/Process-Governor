# Startup Diagnostics And Functionality Test Plan

## Analysis

The startup path used to initialize settings, logs, automations, page view models, the automation engine, and process monitoring silently before the main window opened. That made startup failures hard to understand and left no structured proof that the app was ready.

This branch moves startup into explicit diagnostics:

- Local data folders are created and verified.
- Settings are loaded before settings-bound view models are resolved.
- Persistent logs are loaded and a startup entry is written.
- Automation rules and profiles are loaded, with profile rule references checked.
- Automation engine initialization is verified before monitoring starts.
- A one-shot process snapshot validates process enumeration and summary metrics.
- GPU summary metrics are checked through the existing PDH-backed service and allowed to report `Unavailable`.
- Foreground window detection is checked for Phase 3 triggers.
- Primary page view models are initialized before the main UI opens.

The loading window reports each check as `WAIT`, `RUN`, `PASS`, `WARN`, or `FAIL`. Fatal failures stop startup; non-fatal capability gaps, such as unavailable GPU counters, are shown and logged as warnings.

## Logging Changes

Startup diagnostics now write structured log entries for every completed check. Process actions also log safe failures, including access denied, invalid affinity masks, protected process blocks, missing executable paths, and process race conditions.

## Automated Test Harness

`ProcessGovernor.TestHarness` is a no-NuGet command-line harness so tests can run even when package restore is restricted.

Covered checks:

- JSON persistence round-trip for typed settings.
- Default automation rules and profiles are created without broken profile references.
- Rule evaluation for process, CPU threshold, window title, and fullscreen triggers.
- Startup diagnostics complete without fatal failures in headless mode.
- GPU metrics service either reports measured data or explicit `Unavailable`, never a fake value.
- Process action service safely changes priority and kills a disposable child process, and rejects invalid affinity masks.

Run it with:

```powershell
dotnet run --project .\ProcessGovernor.TestHarness\ProcessGovernor.TestHarness.csproj
```

## Smoke Test Checklist

Before merging this branch, run:

```powershell
dotnet build .\ProcessGovernor.sln
dotnet run --project .\ProcessGovernor.TestHarness\ProcessGovernor.TestHarness.csproj
```

Then launch the desktop app and verify:

- The startup diagnostics window appears before the main UI.
- Startup checks advance visibly and log their result.
- The main window opens only after diagnostics pass.
- Dashboard process rows populate.
- Logs page shows startup diagnostic entries.
- Closing the app does not leave a build-locking process behind when explicitly stopped.
