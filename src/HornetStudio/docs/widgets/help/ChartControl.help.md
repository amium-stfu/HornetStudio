# ChartControl Help

## Widget Type

`ChartControl`

## Overview

The ChartControl widget renders live time-series data based on configured chart series definitions. The implementation is provided by the RealtimeChart control.
It is display-only with respect to history ownership.

## Properties

### ChartSeriesDefinitions

Stores the configured chart series and their source bindings.

### ViewSeconds

Defines the visible time range for the plot.

### View

Allows the chart to participate in page view selection.

## Functions and Behavior

### Configure plot

The widget initializes axes, series, and plot host state when attached.

### Render plot

A timed render loop updates the visible plot while the page is active.
The control consumes render-ready snapshots and does not build raw history arrays during rendering.

### Hook chart item

The widget observes the bound item and reacts to data changes.

### Runtime ownership

ChartControl owns only display concerns such as selected series, view range, axes, and rendering cadence.
Sampling and retention are owned by the folder-scoped signal history runtime and only for Signal widgets with `HistorySeconds > 0` and `RefreshRateMs > 0`.
If a chart references a source without enabled signal history, the series remains empty and no implicit recording starts.

### Crosshair and inspection

The chart supports visual inspection helpers such as crosshair overlays.

## Runtime Notes

The persisted widget type is `ChartControl`, while the code implementation is `RealtimeChartControl`.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/ChartControl.md`
- Help file: `src/HornetStudio/docs/widgets/help/ChartControl.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/RealtimeChart/RealtimeChartControl.axaml.cs`