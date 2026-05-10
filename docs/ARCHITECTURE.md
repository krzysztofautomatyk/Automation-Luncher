# AutomationLauncher — Architecture

## Overview
AutomationLauncher is a Windows desktop tray application (.NET Framework 4.8, WPF) for automating Siemens TIA Portal project archiving. It follows **Clean Architecture** with strict layer separation. The App layer is split into a non-WPF support library (`AutomationLauncher.App.Core`) and the WPF shell (`AutomationLauncher.App`) so that settings infrastructure and script orchestration stay testable outside the UI host.

## Layer Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│  AutomationLauncher.App  (net48, WPF shell)                     │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────────────┐   │
│  │  Shell/     │  │  ViewModels/ │  │  Views/              │   │
│  │  (App.*.cs) │  │  (MVVM)      │  │  (WPF windows)       │   │
│  └─────────────┘  └──────────────┘  └──────────────────────┘   │
└──────────────────────────┬──────────────────────────────────────┘
                           │ depends on
┌──────────────────────────▼──────────────────────────────────────┐
│  AutomationLauncher.App.Core  (net48, non-WPF App services)     │
│  ┌─────────────────────┐  ┌──────────────────────────────────┐  │
│  │  Settings/          │  │  Services/ / ProjectScripts/     │  │
│  │  Protected store    │  │  Control-file orchestration      │  │
│  └─────────────────────┘  └──────────────────────────────────┘  │
└──────────────────────────┬──────────────────────────────────────┘
                           │ depends on
┌──────────────────────────▼──────────────────────────────────────┐
│  AutomationLauncher.Application  (netstandard2.0)               │
│  ┌──────────────────────────────────────┐                       │
│  │  UseCases/ArchiveProjectUseCase.cs   │                       │
│  └──────────────────────────────────────┘                       │
└──────────────────────────┬──────────────────────────────────────┘
                           │ depends on
┌──────────────────────────▼──────────────────────────────────────┐
│  AutomationLauncher.Domain  (netstandard2.0)                    │
│  ┌────────────────────┐  ┌─────────────────────────────────┐    │
│  │  Contracts/        │  │  Models/                        │    │
│  │  ITiaPortalGateway │  │  ArchiveOptions, ArchiveResult  │    │
│  │  IPathService      │  │  TiaProjectContext, etc.        │    │
│  │  IOperationLogger  │  └─────────────────────────────────┘    │
│  │  ITiaRuntimeCatalog│                                         │
│  └────────────────────┘                                         │
└──────────────────────────┬──────────────────────────────────────┘
                           │ implements
┌──────────────────────────▼──────────────────────────────────────┐
│  AutomationLauncher.Infrastructure  (net48)                     │
│  ┌─────────────────────┐  ┌──────────────────────────────────┐  │
│  │  Tia/               │  │  FileSystem/ / Logging/          │  │
│  │  TiaPortalGateway   │  │  PathService, OperationLogger    │  │
│  │  (Openness API)     │  └──────────────────────────────────┘  │
│  │  TiaPortalRuntimeCatalog                                     │
│  └─────────────────────┘                                        │
└─────────────────────────────────────────────────────────────────┘
```

## Dependency Rules
| Layer | May depend on |
|---|---|
| Domain | Nothing (BCL only) |
| Application | Domain |
| Infrastructure | Domain, Application |
| App.Core | Domain |
| App | App.Core, Domain, Application, Infrastructure |

Violations of these rules are build errors (enforced by project references).

## Key Flows

### Archive Flow
```
User / configured archive control file (default: .march)
    → App.HostControl.cs: HandleArchiveControlCommandDetectedAsync
    → App.Archive.cs: RunArchiveWithCountdownAsync (UI countdown + splash)
    → MainWindowViewModel.RunArchiveFromControlFileWithResultAsync
    → ArchiveProjectUseCase.ExecuteAsync
        1. ITiaPortalGateway.GetCurrentContextAsync  — detect TIA process
        2. ITiaPortalGateway.CheckOnlineStateAsync   — verify PLC online
        3. ITiaPortalGateway.CompareOnlineOfflineAsync — online/offline 1:1 check
        4. ITiaPortalGateway.GoOfflineAsync           — switch PLC offline
        5. ITiaPortalGateway.SaveProjectAsync         — save before archive
        6. ITiaPortalGateway.ArchiveProjectAsync      — create .zap archive
    → ArchiveResult returned to App shell
    → App writes .archok control file
```

### Host Control File Protocol
```
External system writes configured archive command file (default: HOSTNAME.march)
App timer (3s poll) detects file → consumes it → runs archive flow
App writes HOSTNAME.archok or HOSTNAME.error
```
Full protocol: `docs/HOST_CONTROL_FLOW.md`

### Startup Sequence
```
App starts → reads appsettings.json + protected-settings.json
    → DI container built (Host)
    → Tray icon initialized
    → Host control files initialized
    → If --startup-launch arg: runs startup automation
    → Optionally shows main window
```

## Settings Architecture
Settings are layered:
1. `appsettings.json` — default values (shipped with app)
2. `%LocalAppData%\AutomationLauncher\settings-cache.json` — cached settings (written on save)
3. `%LocalAppData%\AutomationLauncher\protected-settings.json` — AES-256-CBC encrypted settings
4. `%LocalAppData%\AutomationLauncher\user-settings.json` — legacy path read only for migration and removed when protected/cache files are already present

The protected settings file uses PBKDF2/SHA256 (100,000 iterations) for key derivation and constant-time comparison for password verification.

## Technology Stack
| Component | Technology |
|---|---|
| UI Framework | WPF (.NET Framework 4.8) |
| App support library | `AutomationLauncher.App.Core` (net48) |
| MVVM | CommunityToolkit.Mvvm 8.2.2 |
| DI Container | Microsoft.Extensions.DependencyInjection |
| Hosting | Microsoft.Extensions.Hosting |
| Logging | Serilog (File + Console, rolling daily) |
| Resilience | Polly (exponential backoff retry for TIA ops) |
| TIA Integration | Siemens TIA Portal Openness API (reflection-loaded) |
| Tests | xUnit, Fake objects (no mock framework) |

## Adding a New Use Case
1. Define any new contract interfaces in `Domain/Contracts/`
2. Define result/model types in `Domain/Models/`
3. Implement the use case in `Application/UseCases/MyUseCase.cs`
4. Implement infrastructure in `Infrastructure/`
5. Register in `Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
6. Call from `App` layer ViewModel or Shell
7. Write tests in `Application.Tests/MyUseCaseTests.cs` using Fake objects
