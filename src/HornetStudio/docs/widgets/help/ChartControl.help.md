# ChartControl Help

## Widget Type

`ChartControl`

## Overview

The ChartControl widget renders live time-series data based on configured chart series definitions. The implementation is provided by the RealtimeChart control.

## Properties

### ChartSeriesDefinitions

Stores the configured chart series and their source bindings.

### HistorySeconds

Defines the amount of history retained for plotting.

### ViewSeconds

Defines the visible time range for the plot.

### View

Allows the chart to participate in page view selection.

## Functions and Behavior

### Configure plot

The widget initializes axes, series, and plot host state when attached.

### Render plot

A timed render loop updates the visible plot while the page is active.

### Hook chart item

The widget observes the bound item and reacts to data changes.

### Crosshair and inspection

The chart supports visual inspection helpers such as crosshair overlays.

## Runtime Notes

The persisted widget type is `ChartControl`, while the code implementation is `RealtimeChartControl`.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/ChartControl.md`
- Help file: `src/HornetStudio/docs/widgets/help/ChartControl.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/RealtimeChart/RealtimeChartControl.axaml.cs`