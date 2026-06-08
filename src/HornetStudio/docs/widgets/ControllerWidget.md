# ControllerWidget

## Type

`ControllerWidget`

## Status

This legacy widget type has been removed from the editor picker and canvas rendering.
Manage controllers through the Admin `Controllers` browser and folder-local `Controllers/*.yaml` files instead.

## Legacy Purpose

The removed widget used to manage PID controller definitions and publish each controller as runtime items. Controller definitions now belong in folder-local `Controllers/*.yaml` files and are managed through the Admin-only `Controllers` browser.

## Current Use

- PID loop configuration inside a folder
- Owned runtime setpoint control through the direct value of each controller `set` item
- Runtime start and stop control through the direct value of each controller `run` item
- Scaled and clamped output writes to a configured target path
- File-backed controller management via the Admin `Controllers` browser

## Key Configuration

- Controller name
- Source process value path
- Output target path
- PID tuning values `Ks`, `Tu`, `Tg`, and `DFilterTauMs`
- Setpoint and output ranges
- Compute and output intervals
- Editor spacing and field tooltips help distinguish adjacent PID parameters

## Runtime Notes

Each configured PID controller publishes a runtime root below:

`studio.<folder>.controller.<controller_name>`

The runtime exposes `run`, `read`, `set`, `out`, `state`, `alert`, and `parameters` children. `Read` contains the resolved process value from the configured source path and `Out` stays bound to the configured writable picker path. `Set` is owned by the controller runtime itself at `studio.<folder>.controller.<controller_name>.set` and stores the controller-owned setpoint directly in its item value. Writing `true` to the direct value of `run` starts evaluation and writing `false` stops it.

`out` publishes the normalized controller output in percent. `out.scaled` publishes the scaled value that is written to the configured output target. `alert` is a boolean fault flag for monitor conditions, while `state` carries the human-readable runtime status or fault reason.

## Storage

Controller definitions are loaded from `Controllers/*.yaml` files in the folder directory. Definitions embedded in a placed `ControllerWidget` remain legacy fallback only. File-backed definitions take precedence on duplicate names.

## Controllers Browser

The Admin-only `Controllers` entry in the right legend browser panel opens the `ControllersBrowserDialogWindow`. The browser lists all `Controllers/*.yaml` files for the selected folder and supports add, edit, and delete without requiring a placed widget on the canvas. Opening and closing the browser does not stop or release running controller runtimes.

## Source

- `src/HornetStudio.Editor/Persistence/Controller/`
- `src/HornetStudio.Host/PidControllerRuntime.cs`
- `src/HornetStudio.Host/ControllerRuntimeManager.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/ControllerWidget.help.md`
