# MonitorView Help

## Widget Type

`MonitorView`

## Overview

`MonitorView` is an explicit alarm/status widget for folder-local monitor rules. It shows only the monitor rule names listed in `SelectedMonitorIds` and never edits `Monitoring/Monitor.yaml` itself.

Use Monitor Browser for management and debug overviews. `MonitorView` is intended for placed operator pages where each visible row is intentionally selected.

## Properties

### SelectedMonitorIds

Selected monitor rule names to display.

The editor exposes this property through a registry-backed multi-select picker. Persisted values stay on stable rule names, not numeric `EventId` values.

Example:

- `pressure_low, temperature_high`
- `monitor_rule_1`

The widget normalizes rule names to `snake_case` and ignores duplicates. Existing numeric values such as `1` can still be matched for compatibility, but they are not the intended persisted format.

When no rules are selected, the widget stays empty instead of showing all available monitor rules.

### OnActiveColor

Optional color override for active rows.

- Empty value keeps the theme-based accent background
- Non-empty value is applied only while a row is active
- Inactive rows keep the normal body background

## Behavior

- Loads available monitor definitions through the shared `MonitorRegistry`
- Reads optional folder-local runtime settings from `Monitoring/Monitor.yaml`, including `runtime.startupDelayMs`
- Resolves runtime state through `studio.{FolderName}.monitor.{RuleName}`
- Creates one alert row per selected rule and binds each row only to the exact selected rule root item
- Displays one line per row: `EventId` plus `EventText` from `Monitoring/Monitor.yaml`
- Uses the interpreted root bool value only for active/inactive styling
- Displays only selected rows
- Ignores runtime events for unselected rules before scheduling UI refresh work
- Ignores folder aggregate monitor events such as `studio.{FolderName}.monitor`
- Ignores descendant updates such as `studio.{FolderName}.monitor.{RuleName}.active` and `.message`
- Uses the picker label for selection context: `EventId - EventText (Name)`
- Hides add/edit/delete actions
- Picks up delayed root publication after startup without requiring a broad widget refresh
- Ignores legacy descendant runtime items such as `studio.{FolderName}.monitor.{RuleName}.active` and `.message`; the intended signal source is the rule root bool item itself
- Receives root updates only for the initial publish and later bool transitions; unchanged active/inactive evaluations do not republish the rule root
- Reacts to actions only when the underlying monitor rule changes from inactive to active

## Notes

Use `MonitorView` for operator-facing pages that should react to monitor state without exposing the full monitor editor.
