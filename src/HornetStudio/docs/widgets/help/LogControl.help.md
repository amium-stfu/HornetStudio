# LogControl Help

## Widget Type

`LogControl`

## Overview

The LogControl widget displays log output on a page and can be used for host or process-oriented runtime diagnostics.

## Properties

### TargetLog

Defines the log path that should be displayed.

### View

Controls in which page view the widget is active.

### Header / Footer / Body styling

Common visual shell properties for layout and theme integration.

## Functions and Behavior

### Display log stream

The widget shows entries from the selected log source.

### Integrate with page views

The widget can be shown or hidden depending on the active view.

### Process log support

A related process-log variant exists for process-oriented output rendering.

## Runtime Notes

LogControl is intended for in-page diagnostics and monitoring surfaces.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/LogControl.md`
- Help file: `src/HornetStudio/docs/widgets/help/LogControl.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/Log/EditorLogControl.axaml.cs`
- `src/Hornetstudio.Editor/Widgets/Log/EditorProcessLogControl.axaml.cs`