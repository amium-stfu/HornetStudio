# WidgetList Help

## Widget Type

`WidgetList`

## Overview

The WidgetList widget hosts child widgets in a vertical list and can enforce shared width and height rules.

## Properties

### ListItemHeight

Defines the default or shared child item height.

### IsAutoHeight

Controls whether the list automatically synchronizes child heights.

### ControlBorderWidth / ControlBorderColor / ControlCornerRadius

Shared visual settings propagated to child widgets.

### Items

The child widget collection managed by the list.

## Functions and Behavior

### Apply list defaults to child

When a child is attached, the list applies shared size and border settings.

### Attach child to list

Sets hierarchy, layout file path, and active view for child items.

### Sync child widths

The list keeps child widths aligned to the available content width.

### Apply entered list height

Allows user-driven updates of list height behavior.

### Sync auto height from child

A child size change can propagate back into the list when auto-height is enabled.

## Runtime Notes

WidgetList is a container widget and is primarily responsible for child layout consistency.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/WidgetList.md`
- Help file: `src/HornetStudio/docs/widgets/help/WidgetList.help.md`

## Source

- `src/Hornetstudio.Editor/Models/PageItemModel.cs`