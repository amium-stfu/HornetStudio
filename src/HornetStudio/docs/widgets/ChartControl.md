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
- Visible time range and history range
- View-related layout settings

## Runtime Notes

The persisted widget type is `ChartControl`, while the rendered control implementation is based on `RealtimeChartControl`.

## Source

- `src/Hornetstudio.Editor/Widgets/RealtimeChart/`
- `src/Hornetstudio.Editor/Widgets/RealtimeChart/RealtimeChartControl.axaml.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/ChartControl.help.md`