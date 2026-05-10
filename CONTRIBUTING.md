# Contributing to AutomationLauncher

## Getting started

1. Install the .NET SDK pinned in `global.json`.
2. Clone the repository and work from the repo root.
3. Restore, build, and test before opening a pull request:
   1. `dotnet restore "Automation Luncher.sln"`
   2. `dotnet build "Automation Luncher.sln" -c Release`
   3. `dotnet test "Automation Luncher.sln" -c Release`

## Repository layout

| Path | Purpose |
|---|---|
| `src/AutomationLauncher.Domain` | Contracts and pure models |
| `src/AutomationLauncher.Application` | Use cases and application services |
| `src/AutomationLauncher.Infrastructure` | TIA, file system, logging adapters |
| `src/AutomationLauncher.App.Core` | Non-WPF App-layer services and settings infrastructure |
| `src/AutomationLauncher.App` | WPF shell, views, view models, tray host |
| `tests/` | Per-layer test projects |
| `docs/` | Architecture, operations, host-control protocol |

## Architecture rules

- Keep business rules out of WPF views and view models.
- Add cross-layer interfaces in `Domain`, orchestration in `Application`, implementations in `Infrastructure`.
- Treat `AutomationLauncher.App` as the thinnest possible shell.
- Prefer fake objects in tests over mocking frameworks.

## Pull requests

- Keep changes focused and cohesive.
- Update docs when behavior, structure, or setup changes.
- Use conventional commit style in PR titles when practical: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`.
- Call out any TIA Openness prerequisites or machine-specific assumptions.

## AI-assisted development

- Repository-specific guidance for coding agents lives in `.github/copilot-instructions.md`.
- When moving logic, preserve layer boundaries and keep namespaces stable unless there is a strong reason to change them.
