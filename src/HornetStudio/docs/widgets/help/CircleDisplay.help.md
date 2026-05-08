# CircleDisplay Help

## Widget Type

`CircleDisplay`

## Overview

The CircleDisplay widget is a circular visualization control that can expose runtime values for signal state and progress-related presentation.

## Properties

### SignalColor

Defines the color used for the circular signal visualization.

### SignalRun

Represents the running state used by the display.

### ProgressBar

Enables the progress bar layer.

### ProgressState

Stores the current progress value.

### ProgressBarColor

Defines the progress bar color.

### TableRows / TableColumns

CircleDisplay uses table-like layout metadata for its internal visible cell logic.

## Functions and Behavior

### Publish display runtime values

The widget publishes signal color and progress-related values into runtime data.

### Refresh runtime values on property change

Changing the display-related properties triggers publication updates.

### Visibility rules for circular cells

The control can determine whether a conceptual cell lies inside the circular display area.

## Runtime Notes

CircleDisplay runtime values are published as child items so other runtime features can consume them.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/CircleDisplay.md`
- Help file: `src/HornetStudio/docs/widgets/help/CircleDisplay.help.md`

## Source

- `src/Hornetstudio.Editor/Models/PageItemModel.cs`