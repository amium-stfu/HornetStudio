# Item Help

## Widget Type

`Item`

## Overview

The Item widget shows a bound target value with formatting, unit display, body captions, and optional writeback support.

## Properties

### TargetPath

Defines which runtime item should be resolved.

### TargetPropertyPath

Selects the property shown or edited on the target.

### TargetPropertyFormat

Controls formatting for the visible value.

### Unit

Optional explicit unit override.

### BodyCaption / BodyCaptionPosition / BodyCaptionVisible

Controls the optional caption shown in the body area.

### Header, footer, and theme properties

The common shell properties determine the widget appearance.

### IsReadOnly

Blocks value editing when enabled.

## Functions and Behavior

### Resolve target

The widget resolves its configured target path against the runtime registry.

### Refresh target bindings

Updates display properties when the target changes.

### Open value editor

Writable targets can be edited using the shared value input workflow.

### Apply interaction rules

The widget can execute configured interaction actions against its target or other targets.

### Target writeback

The widget can write values or request values back to the runtime target.

## Runtime Notes

The item widget shares much of its target resolution, formatting, and input behavior with the Signal widget.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/Item.md`
- Help file: `src/HornetStudio/docs/widgets/help/Item.help.md`

## Source

- `src/Hornetstudio.Editor/Models/PageItemModel.cs`