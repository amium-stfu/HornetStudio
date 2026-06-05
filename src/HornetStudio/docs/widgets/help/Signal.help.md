# Signal Help

## Widget Type

`Signal`

## Overview

The Signal widget displays a bound runtime signal and supports typed editing, bool toggling, bit toggling, and interaction-rule-based actions.
It can also react visually to Monitor rule states through widget-level visual rules.

## Properties

### TargetPath

Defines the signal target path to resolve.

### TargetPropertyPath

Stores the displayed property. Normal Signal widget editing keeps this field hidden and defaults it to `read` when the target exposes a `read` property.

### TargetPropertyFormat

Controls display formatting.

### Unit

Optional unit override for the displayed value.

### HistorySeconds

Defines how many seconds of in-memory history are retained for this signal source.
`0` disables history recording for the widget.

### RefreshRateMs

Defines how often the widget refreshes its display.
When `HistorySeconds > 0`, the same value also defines how often the central signal history runtime records this signal source.
`0` disables periodic refresh and therefore also disables history recording.

### InteractionRules

Defines additional click-based behavior such as open editor, set value, toggle bool, open or close `DialogWidget` overlays, or invoke Python functions.

### VisualRules

Defines Monitor-driven visual state overrides for the signal body background.
Version 1 exposes only `BodyBackColor` with `None` or `Blink` while the referenced Monitor rule is active.

### IsReadOnly

Blocks input actions when enabled.

## Functions and Behavior

### Open value dialog

The widget can open the shared value input editor for writable targets.
The body click area remains available when the displayed value is empty.

### Target writeback

The target picker selects an item path, not a property path. When a target item exposes a `write` property, user input is written to `write` automatically while the widget continues to display the selected property, typically `read`. Targets without a `write` property fall back to writing the displayed `read` value.

### Toggle bits

Bit-oriented property presentations can route user actions to bit toggling logic.

### Send bool input

Bool-oriented UI choices can send direct input values.

### Execute interactions

The widget can execute configured interaction rules for body and sub-control actions.
`OpenDialog` and `CloseDialog` accept a dialog `Screen` id from the current folder and show or hide the matching internal overlay.

### Apply visual rules

The Action tab also exposes a `Visual` section.
When a referenced Monitor rule runtime path becomes active, the widget can override `BodyBackColor` and optionally blink until the rule clears.

### Respect editor mode

The widget suppresses runtime interaction behavior when edit mode is active.

### Enable chart history recording

Signal widgets own chart history recording.
When history is enabled, the folder-scoped runtime records only the configured signal sources from Signal widgets.
Charts can display that history but do not start or extend recording implicitly.

### UDL live value pipeline

Attached UDL Signal widgets resolve their target binding once, keep metadata and writeback on the public registry path, and read high-frequency live values from a shared runtime live-value store.
Visible widgets then apply the latest available value on a shared UI scheduler cadence instead of reacting to every registry value event individually.
Structural changes such as rebinds, datatype changes, reconnects, or missing-target recovery still use the structural registry path.

## Runtime Notes

Signal behavior builds on the shared target binding and property presentation infrastructure defined in the item model and property control.
If multiple Signal widgets reference the same source, the runtime keeps the longest requested retention and the fastest requested recording interval for that shared source.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/Signal.md`
- Help file: `src/HornetStudio/docs/widgets/help/Signal.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/Signal/EditorSignalControl.axaml.cs`
- `src/Hornetstudio.Editor/Widgets/Property/PropertyControl.axaml.cs`
- `src/Hornetstudio.Editor/Models/PageItemModel.cs`
