# Operations Guide

## Runtime prerequisites

- Windows station with Siemens TIA Portal v19 installed.
- TIA Openness API enabled with appropriate access rights.
- Application user must have write access to archive output directory.

## Startup checklist

1. Verify Archive.ExpectedProjectPath points to the production TIA project path.
2. Verify Archive.ArchiveOutputDirectory exists and is writable.
3. Verify Archive.OpennessAssemblyPath points to Siemens.Engineering.dll from TIA v19 PublicAPI folder.
4. Launch application and confirm status panel is reachable.

## Normal operation

1. Start TIA Portal and open the configured project.
2. Click Archive Now.
3. Validate operation history entry with Success status.
4. Confirm archive file exists in output directory.

## Log diagnostics

- Main log path: logs/automation-launcher-.log
- Search key events:
  - ArchiveStarted
  - TiaContextRead
  - SaveAttempted
  - SaveCompleted
  - ArchiveAttempted
  - ArchiveCompleted
  - ArchiveFailed

## Failure handling

- TIA not running:
  - Start TIA and reopen target project.
- Wrong project open:
  - Close current project and open configured expected project.
- Save failed:
  - Check project lock state and Openness permissions.
- Archive failed:
  - Check output directory permissions and free disk space.
  - Inspect archive retries and related exception in logs.

## Recovery

1. Resolve root cause.
2. Re-run Archive Now.
3. Verify fresh archive file timestamp.
