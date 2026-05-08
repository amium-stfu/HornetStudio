# FolderEditor Help

## Component Type

Editor surface component

## Overview

The FolderEditor component is the main editing surface for placing, selecting, moving, resizing, and adding widgets inside a folder page.

## Properties

### Folder

The currently edited folder model.

### GridLineBrush

Theme-aware grid line color used by the editor surface.

## Functions and Behavior

### BeginSelectionAdd

The editor can switch into add mode for the supported widget control kinds.

### BeginListAdd

The editor can insert supported child types into list controls.

### Selection rectangle

The editor supports drag-based selection over the page canvas.

### Drag and resize workflows

The editor tracks drag origins, resize origins, and grouped selection movement.

### Dialog integration

The component can synchronize with the property dialog window.

## Supported Add Actions

The editor exposes add actions for persisted widget types such as Button, Signal, ListControl, TableControl, CircleDisplay, LogControl, CsvLoggerControl, SqlLoggerControl, ChartControl, CameraControl, UdlClientControl, ApplicationExplorer, CustomSignals, and EnhancedSignals.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/FolderEditor.md`
- Help file: `src/HornetStudio/docs/widgets/help/FolderEditor.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/FolderEditor/FolderEditorControl.axaml.cs`