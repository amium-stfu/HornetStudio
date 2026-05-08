# ValueInput Help

## Component Type

Editor helper component

## Overview

The ValueInput component provides the on-screen editing surface for writable target values.

## Properties

### CardBorderBrush

Border color for the editor card.

### PropertyEditBackgrundColor

Background color used for property editing surfaces.

### PropertyEditForeColor

Foreground color for property text.

### PropertyHoverColor

Hover color used by property selection surfaces.

### ButtonBackColor / ButtonForeColor / ButtonHoverColor

Theme-related button colors for on-screen input pads.

### EditPanelButtonBorderBrush

Border color for edit panel buttons.

## Functions and Behavior

### Select input mode

The control selects text, numeric, hex, or bits input mode based on the target property kind.

### Handle on-screen pad input

The control routes key and action events from the text and numeric input pads.

### Toggle bit values

Bit-oriented editing is supported through toggle buttons.

### Bind to selected item

The editor observes the current item and refreshes the input mode and state when the target changes.

## Runtime Notes

This is an editor support component used by the shared value editing workflow, not a persisted top-level widget type.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/ValueInput.md`
- Help file: `src/HornetStudio/docs/widgets/help/ValueInput.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/ValueInput/EditorValueInputControl.axaml.cs`