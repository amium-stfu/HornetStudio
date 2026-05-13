# Signal Help

## Widget Type

`Signal`

## Overview

The Signal widget displays a bound runtime signal and supports typed editing, bool toggling, bit toggling, and interaction-rule-based actions.

## Properties

### TargetPath

Defines the signal target path to resolve.

### TargetPropertyPath

Stores the displayed property. Normal Signal widget editing keeps this field hidden and defaults it to `read` when the target exposes a `read` property.

### TargetPropertyFormat

Controls display formatting.

### Unit

Optional unit override for the displayed value.

### InteractionRules

Defines additional click-based behavior such as open editor, set value, toggle bool, or invoke Python functions.

### IsReadOnly

Blocks input actions when enabled.

## Functions and Behavior

### Open value dialog

The widget can open the shared value input editor for writable targets.

### Target writeback

The target picker selects an item path, not a property path. When a target item exposes a `write` property, user input is written to `write` automatically while the widget continues to display the selected property, typically `read`. Targets without a `write` property fall back to writing the displayed `read` value.

### Toggle bits

Bit-oriented property presentations can route user actions to bit toggling logic.

### Send bool input

Bool-oriented UI choices can send direct input values.

### Execute interactions

The widget can execute configured interaction rules for body and sub-control actions.

### Respect editor mode

The widget suppresses runtime interaction behavior when edit mode is active.

## Runtime Notes

Signal behavior builds on the shared target binding and property presentation infrastructure defined in the item model and property control.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/Signal.md`
- Help file: `src/HornetStudio/docs/widgets/help/Signal.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/Signal/EditorSignalControl.axaml.cs`
- `src/Hornetstudio.Editor/Widgets/Property/PropertyControl.axaml.cs`
- `src/Hornetstudio.Editor/Models/PageItemModel.cs`
