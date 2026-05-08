# TableControl Help

## Widget Type

`TableControl`

## Overview

The TableControl widget is a grid-based container that positions child widgets by row, column, and span.

## Properties

### TableRows / TableColumns

Define the grid size.

### TableCellRow / TableCellColumn

Define a child widget position inside the table.

### TableCellRowSpan / TableCellColumnSpan

Define how many rows or columns a child occupies.

### Items

Child widget collection contained by the table.

## Functions and Behavior

### Refresh table cell slots

Rebuilds the conceptual cell collection for the table.

### Update cell content from children

Maps child widgets onto their occupied cells.

### Sync table child heights

Updates child heights proportionally based on row count and row span.

### Add supported child controls

The table can host supported child widget kinds such as item, button, chart, log, and UDL client controls.

## Runtime Notes

TableControl is a structural container and manages layout-related behavior rather than domain-specific runtime logic.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/TableControl.md`
- Help file: `src/HornetStudio/docs/widgets/help/TableControl.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/Table/EditorTableControl.axaml.cs`
- `src/Hornetstudio.Editor/Models/PageItemModel.cs`