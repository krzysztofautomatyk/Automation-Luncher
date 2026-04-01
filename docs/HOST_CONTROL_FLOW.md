# Host Control Flow

This document defines the hostname control-file protocol used by Automation Launcher.

`HOST` means the current Windows machine name, for example `DESKTOP-S5T00FK.run`.

## Supported Files

Command files:

- `HOST.start`
- `HOST.stop`
- `HOST.march`

Runtime/result files:

- `HOST.run`
- `HOST.ready`
- `HOST.archok`
- `HOST.error`

No other hostname control files are supported.

## Core Rules

1. While the launcher process is alive, `HOST.run` must exist.
2. If `HOST.run` is deleted externally, the launcher recreates it automatically.
3. On launcher shutdown, `HOST.run` is removed.
4. When a command file is consumed, the launcher deletes all hostname control files except `HOST.run` before executing that command.

## Command Flow

### `HOST.start`

1. Detect `HOST.start`.
2. Delete `HOST.start`.
3. Delete all control files except `HOST.run`.
4. Start managed startup automation.
5. If an operational error occurs, create `HOST.error`.

### `HOST.stop`

1. Detect `HOST.stop`.
2. Delete `HOST.stop`.
3. Delete all control files except `HOST.run`.
4. Run stop workflow for managed applications.
5. On successful stop, create `HOST.ready`.
6. If an operational error occurs, create `HOST.error`.

### `HOST.march`

1. Detect `HOST.march`.
2. Delete `HOST.march`.
3. Delete all control files except `HOST.run`.
4. Run archive workflow.
5. On successful archive, create `HOST.archok`.
6. If an operational error occurs, create `HOST.error`.

## Error Marker

`HOST.error` is created when the launcher encounters operational errors in command handling, startup automation, or other guarded runtime paths.

## Tray Indicator Colors

- Green blinking: startup automation (`HOST.start`, Windows startup launch sequence, or manual startup run).
- Orange blinking: stop workflow (`HOST.stop` or manual stop).
- Blue blinking: archive workflow (`HOST.march` or manual archive).
- Red blinking: error state (`HOST.error` detected or created).
