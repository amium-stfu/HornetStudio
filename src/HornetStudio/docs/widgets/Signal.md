# Signal Widget

## Type

`Signal`

## Purpose

Displays a bound signal value and can open editors or execute interactions against its target.

## Typical Use Cases

- Show live signal values
- Toggle bool or bit-based values
- Open value editors for runtime input

## Key Configuration

- Target path
- Target property and format
- Display refresh and history retention
- Unit and caption
- Interaction rules
- Header, body, and footer settings

## Runtime Notes

The signal widget uses property visualization and can open typed value editors or send direct input to the configured target.

The body remains interactive even when the displayed value is empty.

Target selection resolves an item path. The displayed property defaults to `read` when available. User input is routed automatically to the same item's `write` property when that property exists; otherwise the widget falls back to writing the displayed `read` value.

For attached UDL signals, steady-state live values are resolved once and then read from a shared runtime live-value store on a shared UI cadence. This keeps visible Signal widgets updated without depending on one registry-dispatch roundtrip per incoming value and per widget.

When `HistorySeconds` is greater than `0`, the widget uses `RefreshRateMs` as both its display refresh cadence and the central signal-history recording cadence for its configured source. That recorded history can then be consumed by ChartControl widgets without making the chart itself a recording owner.

## Source

- `src/Hornetstudio.Editor/Widgets/Signal/`
- `src/Hornetstudio.Editor/Widgets/Signal/EditorSignalControl.axaml.cs`
- `src/Hornetstudio.Editor/Widgets/Property/PropertyControl.axaml.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/Signal.help.md`
