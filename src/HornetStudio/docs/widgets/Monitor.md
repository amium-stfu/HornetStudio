# Monitor Widget

## Type

`Monitor`

## Purpose

Monitors item values and state conditions, publishes rule-based runtime status entries, and exposes transition-based event actions.
The folder-level monitor runtime continues in the background even when no Monitor browser is open on the canvas.

## Typical Use Cases

- Timeout monitoring for item updates
- Lower and upper limit supervision
- Custom boolean formula checks with rule-local variables
- Transition-driven actions such as `WriteLog`, `SetValue`, and `InvokeFunction`
- Active event aggregation by severity or log level

## Key Configuration

- Rule definitions stored centrally in `Monitoring/Monitor.yaml`
- The `Monitor` widget acts as a browser and editor for the folder-local monitor registry instead of owning the authoritative rule list itself
- Each rule owns its own refresh rate, timeout, inhibit window, mode, event metadata, action list, and log level
- `EventId` is required, must be a positive integer, and must be unique within the same folder monitor registry
- New rules auto-assign the smallest free positive `EventId`
- The rule editor opens with separate `Rule` and `Actions` tabs so condition setup and transition behavior stay readable
- `Default` mode evaluates configured lower and upper limits
- `Custom` mode evaluates a boolean formula built only from rule variables
- The direct source picker is available only in `Default` mode; `Custom` mode uses variable source pickers inside the formula section
- `Custom` mode now uses the shared boolean condition editor with variable rows, source picking, token buttons, and live validation
- Actions run on `OnActivated` and `OnCleared` transitions and execute in configured order
- `WriteLog` uses a log target, `SetValue` uses an item target plus argument, and `InvokeFunction` uses an application target, function name, and optional argument
- Rule names define the stable published runtime path segments

## Runtime Notes

Each rule publishes its state below `studio.{FolderName}.monitor.{RuleName}`.
The folder also publishes monitor-level aggregates below `studio.{FolderName}.monitor` such as `fatal_active`, `error_active`, and `warning_active`.
Each aggregate item keeps the value payload as a comma-separated list of active `EventId` values and also writes `Properties["meta"]` as JSON, for example `{"events":[{"event_id":123,"text":"RangeError"}]}`.
When no rules are active for a level, the aggregate value is an empty string and `meta` is `{"events":[]}`.
Rules are loaded from the folder-local registry file.
The shared folder runtime manager owns evaluation, publication, and transition actions independently from the `Monitor` widget lifecycle.
Opening a `Monitor` widget only shows and edits the current folder registry state; closing the widget does not stop monitor execution.
When no `Monitor` widget is placed, the folder still keeps monitor runtime publication active through the shared monitor runtime manager.
Timeout checks are always available. Actions run only on state transitions and are not repeated while a rule remains active.

## Editor Layout

- `Rule` tab: name, mode, cadence, timeout, inhibit, event metadata, default limits, custom variables, and formula
- `Actions` tab: configured transition actions with trigger, action type, target, optional function selector, optional argument, and add/remove controls
- Multiple actions can share the same trigger and are executed in the configured order
- The widget body shows each configured rule as one compact row with `EventId`, `EventText`, and a compact row action menu
- Active rows use a severity-colored border and a subtle severity-tinted background so state is visible without a dedicated LED
- Body rows are display-sorted by severity: `Fatal`, `Error`, `Warning`, `Info`, `Debug`
- When `EventText` is empty, the row falls back to the rule name
- `Edit` and `Delete` are available from each row's compact action menu and write back to `Monitoring/Monitor.yaml`

## Source

- `src/HornetStudio.Editor/Widgets/Monitor/`
- `src/HornetStudio.Editor/Widgets/Monitor/MonitorControl.axaml.cs`
- `src/HornetStudio.Editor/Widgets/Monitor/MonitorEditorDialogWindow.axaml`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/Monitor.help.md`
