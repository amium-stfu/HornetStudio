# Monitor Help

## Widget Type

`Monitor`

## Overview

The Monitor widget evaluates rule definitions against runtime items and publishes a dedicated status subtree for each rule. Rules can watch missing updates, numeric thresholds, custom boolean expressions, and execute actions on state transitions.
The runtime is folder-scoped and continues in the background independently from whether a Monitor browser widget is currently open.

## Properties

### MonitorDefinitions

Legacy widget-local storage for monitor rules.

Each rule stores its own refresh rate, timeout, inhibit delay, mode, limits, custom variables, formula, event metadata, action list, and log level.
`EventId` is mandatory, must be greater than `0`, and must be unique within the current folder monitor registry. New rules start with the smallest free positive `EventId`.

The authoritative monitor rules now live in `Monitoring/Monitor.yaml`. Existing `Properties.MonitorDefinitions` values remain compatibility input when no central monitor file exists yet.

### Monitoring/Monitor.yaml

Stores the authoritative folder-local monitor registry.

The `Monitor` widget reads and edits this file. When the file exists, it overrides legacy widget-local monitor definitions. A folder can therefore continue to evaluate and publish monitor state even without a placed `Monitor` widget.

### Name / Path / FolderName

These identity values affect the generated runtime paths for published monitor state.

## Functions and Behavior

### Edit rule tabs

The Monitor rule dialog is split into two tabs:

- `Rule` contains name, mode, refresh settings, timeout, inhibit, event metadata, limits, variables, and formula editing.
- `Actions` contains only transition action editing so multiple `OnActivated` and `OnCleared` actions stay easy to scan.

### Rebuild rules

The widget parses stored monitor definitions and creates visible runtime rows.
The body displays rules as compact one-line entries with `EventId`, `EventText`, and a compact row action menu.
Display order is severity-first: `Fatal`, `Error`, `Warning`, `Info`, `Debug`.
If a rule has no `EventText`, the widget shows the rule name instead.
Inactive rows remain visually neutral until the rule becomes active.
Active rows use a severity-colored border and a subtle severity-tinted background so the active state stays visible in compact layouts.
Each row exposes `Edit` and `Delete` through its compact action menu.
The visible rows are projections of the shared folder runtime; the widget itself does not own the background evaluator.

### Evaluate rule state

Each rule can activate on timeout, lower-limit breach, upper-limit breach, or a custom boolean expression.

- `Default` mode evaluates only configured lower and upper limits.
- `Custom` mode evaluates only the boolean formula.
- Timeout evaluation is independent of the selected mode.
- Invalid or non-boolean formulas fail closed and keep the rule inactive.
- The top-level source picker is shown for `Default` mode. In `Custom` mode, variable source pickers inside the formula section define additional inputs.

### Publish runtime entries

Published monitor paths use the folder-stable format `studio.{FolderName}.monitor.{RuleName}`.
The status snapshot contains active state, event metadata, log level, source path, and current value metadata.
These rule runtime paths can be selected as `VisualRules` sources on other widgets when they need to react to Monitor state.
The shared folder runtime keeps these paths current even if the Monitor widget is closed or not placed at all.

The monitor root `studio.{FolderName}.monitor` also publishes aggregate active event-id lists grouped by log level:

- `debug_active`
- `info_active`
- `warning_active`
- `error_active`
- `fatal_active`

Each aggregate keeps the item value as the comma-separated active `EventId` list. The same aggregate item also publishes `Properties["meta"]` as valid JSON with matching `EventId` and `EventText` pairs, for example `{"events":[{"event_id":123,"text":"RangeError"},{"event_id":124,"text":"DI5 high"}]}`. If no rules are active for that level, the aggregate value is empty and the metadata becomes `{"events":[]}`.

### Inhibit repeated activation

Rules can delay activation using an inhibit window so short spikes do not create immediate warnings.

### Execute transition actions

Rules can execute zero, one, or multiple actions on `OnActivated` and `OnCleared`.

- Matching actions run in configured order.
- `WriteLog` writes one ProcessLog entry using the rule `EventId`, `EventText`, and `LogLevel`.
- `SetValue` writes the configured `Argument` into the configured target item using the same typed write semantics as other interaction rules.
- `InvokeFunction` resolves the configured application target the same way as interaction rules and invokes the selected function with the configured argument payload.
- The `Actions` tab validates each configured row before saving and requires a target log for `WriteLog`, a target path for `SetValue`, and both target path and function name for `InvokeFunction`.
- Legacy persisted `TargetLog` values load as `OnActivated -> WriteLog` for compatibility.

## Help Notes for Users

Use Monitor when a plain Signal widget is not enough and the page needs rule-based supervision. Keep rule names in `snake_case` so runtime paths remain predictable.
Keep `EventId` values unique inside the folder monitor registry so log and aggregate consumers can map active events reliably.

For Custom mode:

- Variables point to runtime items and are referenced as `{VariableName}` inside the formula.
- The top-level source field is hidden in this mode.
- The shared boolean condition editor provides variable rows, source picking, token buttons, and live validation.
- Actions are transition-based; the widget does not repeat log lines while a rule remains active.
- Use the `Actions` tab to configure several log writes for the same trigger when different logs should receive the same state change.
- Use `SetValue` when a monitor transition should update a runtime item once.
- Use `InvokeFunction` when a monitor transition should call a Python application function once and optionally pass a JSON payload or plain argument text.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/Monitor.md`
- Help file: `src/HornetStudio/docs/widgets/help/Monitor.help.md`

## Source

- `src/HornetStudio.Editor/Widgets/Monitor/MonitorControl.axaml.cs`
- `src/HornetStudio.Editor/Widgets/Monitor/MonitorEditorDialogWindow.axaml`
