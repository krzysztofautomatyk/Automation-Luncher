# AutomationLauncher

Production-ready desktop application for Siemens TIA Portal project archiving workflow with tray-first operation, password-protected settings and runtime-aware diagnostics.

## Features

- Detects if TIA Portal process is running.
- Validates opened project against configured expected project path.
- Handles save policy before archive (dirty-state aware with fallback policy).
- Triggers archive operation and stores the resulting package in configured destination.
- Runs from the system tray instead of the regular taskbar and exposes a context menu for main actions.
- Protects all persisted settings with a startup password created during the first launch.
- Supports autostart registration, startup-folder access and configurable logging directory/level/retention.
- Structured production logging with Serilog (rolling files + retention).
- xUnit test coverage for business workflow.

## Configuration

Default values are defined in `src/AutomationLauncher.App/appsettings.json`. After the first launch the application stores:

- cached settings in `LocalApplicationData/AutomationLauncher/settings-cache.json`
- protected settings in `LocalApplicationData/AutomationLauncher/protected-settings.json`

If legacy `user-settings.json` exists, the app retires it after protected/cache settings become available.

Key settings:

- Archive.ExpectedProjectPath
- Archive.ArchiveOutputDirectory
- Archive.TryDetectUnsavedChanges
- Archive.ForceSaveWhenDetectionUnavailable
- Archive.SaveTimeoutSeconds
- Archive.ArchiveTimeoutSeconds
- Archive.RetryCount
- Archive.RetryDelayMilliseconds
- Archive.TiaVersionSelectionMode
- Archive.PreferredTiaVersion
- Startup.RunOnWindowsStartup
- Logging.DirectoryPath
- Logging.MinimumLevel
- Logging.RetainedFileCountLimit
- Ui.StartHiddenToTray

## Logging

Logs are written to:

- configured log directory, by default logs/automation-launcher-.log

## Repository Layout

| Path | Purpose |
|---|---|
| `src/AutomationLauncher.Domain` | Contracts and pure models |
| `src/AutomationLauncher.Application` | Use cases and application services |
| `src/AutomationLauncher.Infrastructure` | TIA, file system, and logging adapters |
| `src/AutomationLauncher.App.Core` | Non-WPF App-layer services, settings storage, and script orchestration |
| `src/AutomationLauncher.App` | WPF shell, tray integration, views, and view models |
| `tests/` | Layered test projects |
| `docs/` | Architecture and operational documentation |

## Build and Test

Use the SDK pinned in `global.json`.

1. `dotnet restore "Automation Luncher.sln"`
2. `dotnet build "Automation Luncher.sln" -c Release`
3. `dotnet test "Automation Luncher.sln" -c Release`

## Notes

- For full TIA integration, Siemens Openness API assembly path must be configured and accessible.
- TIA Openness security permissions must be enabled on target engineering station.
- If the password is lost, protected settings cannot be decrypted and must be recreated manually.
- Host control-file flow is documented in [docs/HOST_CONTROL_FLOW.md](docs/HOST_CONTROL_FLOW.md).
