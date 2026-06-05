# MonitorView Widget

## Type

`MonitorView`

## Purpose

Displays explicitly selected folder-local monitor rules as compact single-line alarm rows.

## Typical Use Cases

- Show only selected monitor alarms on overview pages
- Reuse central monitor rules without exposing edit actions
- Display runtime monitor state on pages that should stay operator-focused
- Keep the Monitor Browser separate as the management and debug overview

## Key Configuration

- Reads available monitor rules from `Monitoring/Monitor.yaml`
- Supports optional folder-local `runtime.startupDelayMs` in `Monitoring/Monitor.yaml`
- Filters visible rows through `SelectedMonitorIds`
- Edits `SelectedMonitorIds` through a registry-backed multi-select picker in the editor
- Persists stable monitor rule names such as `monitor_rule_1`, not numeric `EventId` values
- Normalizes duplicate selected rule names to one entry
- Supports optional `OnActiveColor` to override the active-row background color
- Uses folder-stable runtime paths such as `studio.{FolderName}.monitor.{RuleName}`
- Does not provide add, edit, or delete actions
- Does not fall back to showing all monitor rules when no rules are selected

## Runtime Notes

`MonitorView` does not own monitor evaluation. It renders already-published monitor runtime state from the shared folder runtime and creates one alert row per selected rule. Each row reads `EventId` and `EventText` from `Monitoring/Monitor.yaml`, subscribes only to the exact selected rule root path `studio.{FolderName}.monitor.{RuleName}`, and derives active styling from that root bool value. Folder aggregate monitor updates such as `studio.{FolderName}.monitor` and descendant items such as `.active` or `.message` are not widget row updates.

Folder-local monitor runtime settings are also loaded from `Monitoring/Monitor.yaml`:

```yaml
runtime:
	startupDelayMs: 1000
rules:
- name: monitor_rule_1
	...
```

`startupDelayMs` delays the first folder-wide monitor evaluation after startup or runtime rebuild. After that delay, each rule publishes its root bool once for the initial state and then only on later bool transitions. Monitor actions execute only on the `false -> true` transition.

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/MonitorView.help.md`
