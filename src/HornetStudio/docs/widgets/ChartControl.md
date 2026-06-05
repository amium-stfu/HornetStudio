# ChartControl Widget

## Type

`ChartControl`

## Purpose

Displays live chart data for configured signal series over time.

## Typical Use Cases

- Trend monitoring
- Multi-series runtime visualization
- Historical signal inspection inside a page

## Key Configuration

- Chart series definitions
- Visible time range
- View-related layout settings

## Runtime Notes

The persisted widget type is `ChartControl`, while the rendered control implementation is based on `RealtimeChartControl`.
ChartControl does not record signal history.
It requests render-ready snapshots for the configured series and the current `ViewSeconds` window.
Signal history is recorded centrally per folder only for Signal widgets that explicitly enable history.

## Source

- `src/Hornetstudio.Editor/Widgets/RealtimeChart/`
- `src/Hornetstudio.Editor/Widgets/RealtimeChart/RealtimeChartControl.axaml.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/ChartControl.help.md`