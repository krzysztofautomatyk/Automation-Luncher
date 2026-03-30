# AutomationLauncher

Production-ready desktop application for Siemens TIA Portal v19 project archiving workflow.

## Features

- Detects if TIA Portal process is running.
- Validates opened project against configured expected project path.
- Handles save policy before archive (dirty-state aware with fallback policy).
- Triggers archive operation and stores the resulting package in configured destination.
- Structured production logging with Serilog (rolling files + retention).
- xUnit test coverage for business workflow.

## Configuration

Edit src/AutomationLauncher.App/appsettings.json:

- Archive.ExpectedProjectPath
- Archive.ArchiveOutputDirectory
- Archive.TryDetectUnsavedChanges
- Archive.ForceSaveWhenDetectionUnavailable
- Archive.SaveTimeoutSeconds
- Archive.ArchiveTimeoutSeconds
- Archive.RetryCount
- Archive.RetryDelayMilliseconds
- Archive.OpennessAssemblyPath

## Logging

Logs are written to:

- logs/automation-launcher-.log

## Build and Test

1. dotnet restore
2. dotnet build -c Release
3. dotnet test

## Notes

- For full TIA integration, Siemens Openness API assembly path must be configured and accessible.
- TIA Openness security permissions must be enabled on target engineering station.
