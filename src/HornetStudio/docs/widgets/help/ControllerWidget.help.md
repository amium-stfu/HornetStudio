# ControllerWidget Help

## Widget Type

`ControllerWidget`

## Status

This legacy widget type is no longer available for placement in the editor.
Use the Admin `Controllers` browser to manage folder-local controller YAML files.

## Overview

Before removal, the ControllerWidget stored PID controller definitions on the folder item and synchronized them with host runtimes. The current path is folder-local `Controllers/*.yaml` files managed through the Admin `Controllers` browser. Each PID controller reads a source value from the configured source path, reads its setpoint from an owned runtime `set` item value, computes a PID output, and writes the scaled result to the configured output target while running.

In the editor, the controller list uses compact single-line rows that show the controller type, controller name, and row actions. Detailed path, parameter, state, and alert text is no longer rendered as permanently expanded multiline content in every row.

## Properties

### ControllerDefinitions

Stores controller definitions as the legacy widget persistence payload. Definitions are now expected in `Controllers/*.yaml` files in the folder directory. File-backed definitions take precedence over widget-embedded ones when both share the same controller name.

### Name / Path / FolderName

Changes to identity or folder context can rebuild the runtime path for controller instances.

### EffectiveBodyBackground / EffectiveBodyBorder / EffectiveBodyForeground / EffectiveMutedForeground

Theme values used by controller rows.

## PID Fields

The PID editor separates adjacent fields with consistent spacing and exposes tooltips on path, tuning, range, and interval inputs.

### Name

Unique controller name within the widget. The normalized name is used in the runtime path.

### Source

Path for the process value read by the controller.

### Set

The PID setpoint is not selected through a picker. Each controller publishes its own runtime item at `studio.<folder>.controller.<controller_name>.set`. Writing a numeric value to that item value updates the setpoint used by the PID loop.

### Output

Target path that receives the scaled and clamped controller output.

### Ks / Tu / Tg

Tuning inputs used to derive the runtime PID parameters.

### D filter tau ms

Derivative filter time constant in milliseconds.

### Compute interval ms

Timer interval for PID evaluation.

### Output interval ms

Minimum interval between output target writes.

### Set min / Set max

Input range used to normalize source and setpoint values.

### Out min / Out max

Output range used to scale the normalized PID result.

## Runtime Behavior

### Published Path

The runtime root is:

`studio.<folder>.controller.<controller_name>`

The runtime publishes the resolved process value as `read`. The `read` value is kept current whenever the configured source path changes, independently of whether the controller is running. The configured source path remains part of the controller definition only and is not published as a separate `source` child.

### Run Control

The runtime publishes `run` as a direct runtime value. Writing `true` to that item value requests a running state; writing `false` stops evaluation and resets integral and derivative state.

### Setpoint Control

The runtime publishes `set` as a direct runtime value. Writing a numeric value changes the PID setpoint owned by that controller runtime. Invalid nonnumeric writes are rejected at evaluation time with a waiting state and alert.

### State and Alerts

The runtime publishes `state` and `alert` values. `alert` is a boolean fault flag. `state` stays human-readable and contains either the normal runtime status such as `Stopped` or `Running`, or the current fault reason when the controller is blocked by invalid input, invalid parameters, or an unavailable output target.

### Output Writes

While running, the runtime writes the scaled controller output to the configured output path through the host registry update API. The runtime item `out` exposes the normalized output in percent, and `out.scaled` exposes the scaled value that is written to the configured target.

## Source

- `src/HornetStudio.Editor/Persistence/Controller/ControllerControl.axaml.cs`
- `src/HornetStudio.Editor/Persistence/Controller/ControllerEditorDialogWindow.axaml.cs`
- `src/HornetStudio.Host/PidControllerRuntime.cs`
