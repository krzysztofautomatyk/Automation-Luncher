# GitHub Copilot Instructions — AutomationLauncher

## Project Overview
AutomationLauncher is a Windows desktop tray application (WPF, .NET Framework 4.8) that automates Siemens TIA Portal project archiving via the TIA Openness API.

## Architecture — Clean Architecture with App split for testability

```
Domain (netstandard2.0)          ← NO external dependencies
  Contracts/        ← Interfaces: ITiaPortalGateway, IPathService, IOperationLogger, ITiaPortalRuntimeCatalog
  Models/           ← Pure data classes: ArchiveOptions, TiaProjectContext, ArchiveResult, etc.

Application (netstandard2.0)     ← Depends only on Domain
  UseCases/         ← ArchiveProjectUseCase (orchestrates gateway + path + logger)

Infrastructure (net48)           ← Implements Domain contracts
  Tia/              ← TiaPortalGateway: Siemens Openness assembly loading + retry via Polly
  FileSystem/       ← PathService implementation
  Logging/          ← OperationLogger implementation

App.Core (net48, non-WPF)        ← App-layer support library
  Settings/         ← AutomationLauncherSettings, ProtectedApplicationSettingsStore (AES)
  Services/         ← Injectable helpers (ControlFileScriptOrchestrator)
  ProjectScripts/   ← PowerShellScriptRunner (control file automation)

App (net48, WPF)                 ← Depends on all layers, DI root
  Shell/            ← Partial App class: tray icon, host control, startup sequence
  Session/          ← SessionCoordinator (activity tracking)
  ViewModels/       ← MVVM with CommunityToolkit.Mvvm
  Views/            ← WPF XAML windows
```

## Layer Rules (STRICT)
- Domain has NO dependencies on any other layer or NuGet packages (except .NET BCL).
- Application depends ONLY on Domain.
- Infrastructure depends on Domain and Application (for interfaces to implement).
- App.Core holds non-WPF App-layer services and settings infrastructure.
- App depends on all layers and owns the DI container root.
- NEVER add business logic to ViewModels — it belongs in use cases.
- NEVER add UI code to Application or Domain layers.
- Prefer App.Core over App for settings storage, script orchestration, and other UI-independent helpers.

## Key Design Patterns
- **Interfaces first**: All cross-layer dependencies use interfaces defined in Domain.
- **Fake objects in tests**: Use `FakeGateway`, `FakePathService`, `FakeOperationLogger` pattern — NOT mocks.
- **Polly retry**: TIA Openness calls use exponential backoff via `AsyncRetryPolicy`. Add retry for any new TIA operation.
- **Structured logging**: Use Serilog with named parameters `{PropertyName}`. Example: `Log.Logger.Information("Archive completed. Path={ArchivePath}", path)`.
- **Nullable enabled**: All projects have `<Nullable>enable</Nullable>`. Handle all nullable paths explicitly.
- **CommunityToolkit.Mvvm**: ViewModels use `[ObservableProperty]` and `[RelayCommand]` source generators.

## Adding New Features

### New use case → `src/AutomationLauncher.Application/UseCases/`
Follow `ArchiveProjectUseCase.cs` pattern:
- Constructor injects `ITiaPortalGateway`, `IPathService`, `IOperationLogger` (from Domain)
- Returns a strongly-typed result object
- Uses `correlationId = Guid.NewGuid().ToString("N")` for log correlation
- Logs every significant step via `IOperationLogger`

### New domain model → `src/AutomationLauncher.Domain/Models/`
- Sealed record or sealed class
- No external dependencies
- No ObservableObject

### New infrastructure service → `src/AutomationLauncher.Infrastructure/`
- Implements a Domain interface
- Registered via `ServiceCollectionExtensions.AddInfrastructure()`
- Uses Polly for any external API calls

### New App-layer service → `AutomationLauncher.App.Core`
- Place it in the App.Core project boundary (`Services/` or `ProjectScripts/` as appropriate)
- Define interface next to the implementation when it is App-layer specific
- Register in `App.Core.cs` ConfigureServices
- Use `IControlFileScriptOrchestrator` as the reference pattern

### New settings property → `src/AutomationLauncher.App.Core/Settings/AutomationLauncherSettings.cs`
- Add to the appropriate nested settings class
- Add documentation comment
- Add default value in the class initializer
- Update `appsettings.json` with the default

## Control File Protocol
The app communicates with external automation systems via hostname-based control files:
- `HOSTNAME.run` — app is running (auto-maintained)
- configured command variants (defaults: `HOSTNAME.start`, `HOSTNAME.stop`, `HOSTNAME.march`) → trigger startup, stop, and archive workflows
- `HOSTNAME.ready` → written after successful stop
- `HOSTNAME.archok` → written after successful archive
- `HOSTNAME.error` → written on errors

See `docs/HOST_CONTROL_FLOW.md` for full protocol specification.

## Testing
- Tests live in `tests/` folder
- `AutomationLauncher.Application.Tests` — use case business logic tests
- `AutomationLauncher.Infrastructure.Tests` — TIA runtime resolution tests
- `AutomationLauncher.App.Tests` — App-layer service tests
- Always use Fake objects (not mocks) — see `FakeGateway` in `ArchiveProjectUseCaseTests.cs`
- Test files must be named `*Tests.cs`

## TIA Openness Specifics
- Siemens Openness API requires `.NET Framework 4.8` — never retarget to net6+
- The API is loaded at runtime via `Assembly.LoadFrom()` — the path is configured in `appsettings.json`
- TIA Openness security must be enabled in TIA Portal options
- `TiaPortalGateway` handles `COMException`, `ObjectDisposedException`, and `Siemens.Engineering.EngineeringException` as retryable errors

## Commit Convention
Use conventional commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`
