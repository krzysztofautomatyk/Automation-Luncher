# Host Control Flow

This document defines the hostname control-file protocol used by Automation Launcher.

`HOST` means the current Windows machine name, for example `DESKTOP-S5T00FK.run`.

## Supported Files

Command files:

- Configurable command variants mapped to one of these actions:
  - `Start`
  - `Stop`
  - `Archive`
- Default command variants:
  - `HOST.start`
  - `HOST.stop`
  - `HOST.march`

Runtime/result files:

- `HOST.run`
- `HOST.ready`
- `HOST.archok`
- `HOST.error`

Runtime/result file names are fixed. Command file suffixes are configurable.

Each configured command can also define its own reaction presentation:

- splash window title
- splash background image
- splash countdown duration
- pre/post PowerShell scripts

## Core Rules

1. While the launcher process is alive, `HOST.run` must exist.
2. If `HOST.run` is deleted externally, the launcher recreates it automatically.
3. On launcher shutdown, `HOST.run` is removed.
4. When a command file is consumed, the launcher deletes all hostname control files except `HOST.run` before executing that command.

## Command Flow

### Start action

1. Detect a configured `Start` command file (default: `HOST.start`).
2. Delete that command file.
3. Delete all control files except `HOST.run`.
4. Start managed startup automation.
5. If an operational error occurs, create `HOST.error`.

### Stop action

1. Detect a configured `Stop` command file (default: `HOST.stop`).
2. Delete that command file.
3. Delete all control files except `HOST.run`.
4. Run stop workflow for managed applications.
5. On successful stop, create `HOST.ready`.
6. If an operational error occurs, create `HOST.error`.

### Archive action

1. Detect a configured `Archive` command file (default: `HOST.march`).
2. Delete that command file.
3. Delete all control files except `HOST.run`.
4. Run archive workflow.
5. On successful archive, create `HOST.archok`.
6. If an operational error occurs, create `HOST.error`.

## Error Marker

`HOST.error` is created when the launcher encounters operational errors in command handling, startup automation, or other guarded runtime paths.

## Tray Indicator Colors

- Green blinking: startup automation (configured `Start` command, Windows startup launch sequence, or manual startup run).
- Orange blinking: stop workflow (configured `Stop` command or manual stop).
- Blue blinking: archive workflow (configured `Archive` command or manual archive).
- Red blinking: error state (`HOST.error` detected or created).
