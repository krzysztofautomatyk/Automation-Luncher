# Changelog

All notable changes to AutomationLauncher are documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- CI/CD workflow (`.github/workflows/ci.yml`) — dotnet build and test on every push/PR
- `.editorconfig` — consistent code style enforcement for C# and config files
- `.github/copilot-instructions.md` — AI-assistant context for architecture, conventions, and TIA specifics
- `docs/ARCHITECTURE.md` — layer diagram, flow documentation, technology stack reference
- `CHANGELOG.md` — this file
- `ControlFileScriptOrchestrator` service — extracted from App partial class, now injectable and testable
- `IControlFileScriptOrchestrator` interface — enables testing of control-file script execution logic
- `AutomationLauncher.App.Tests` test project — covers App-layer services
- `ProjectPathMatchingTests` — covers edge cases for project path matching (case sensitivity, extension variants)
- `ControlFileScriptOrchestratorTests` — covers script binding resolution and outcome routing

### Changed
- `appsettings.json` — replaced hardcoded demo paths with descriptive placeholders
- `App.ControlFileScriptAutomation.cs` — now delegates to `IControlFileScriptOrchestrator` via DI
- `App.Core.cs` — registers new services in DI container

### Removed
- `src/AutomationLauncher.App/AutomationLauncherSettings.cs` — zombie file (superseded by `Settings/AutomationLauncherSettings.cs`)
- `src/AutomationLauncher.App/ProtectedApplicationSettingsStore.cs` — zombie file (superseded by `Settings/ProtectedApplicationSettingsStore.cs`)
- `<Compile Remove>` entries from `AutomationLauncher.App.csproj`

## [10.0.0] — Previous Release

### Added
- Error and warning log counts in UI
- Per-step parameter overrides for control file script bindings
- Script preview in settings window
- Bind control-file script steps restricted to start/stop/march command types
- Password show/hide toggle in password prompt window
- Tabbed navigation in Settings window
- Archive countdown splash with cancel dialog and save countdown
- Control-file subfolder support (`Ui.ControlFilesDirectory` setting)
- `HOST.archok` marker file written after successful archive
- Startup automation sequence with splash window and countdown
- Multi-version TIA Portal runtime support (V17, V18, V19, V20)
- TIA runtime validation matrix documentation
- Host control flow documentation
- Archive backup flow options (TimestampedRetention, StableFileWithOld)
- Settings export/import functionality
- Password-protected settings (AES-256-CBC with PBKDF2/SHA256)
- Single-instance enforcement via global Mutex
- Windows startup registration
- System tray operation with context menu
- Structured production logging with Serilog (rolling files)
- xUnit test coverage for ArchiveProjectUseCase
