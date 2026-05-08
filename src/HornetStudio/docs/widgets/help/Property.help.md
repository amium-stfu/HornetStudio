# Property Help

## Component Type

Editor helper component

## Overview

The Property component renders typed value presentation for signal-like widgets and can expose bool and bit-based interaction UI.

## Properties

### Presentation

The formatted property view model used for display.

### ValueFontSize

Base font size for the main value text.

### UnitFontSize

Base font size for the unit text.

### UnitWidth

Reserved width for the unit area.

### UnitBaselineOffset

Controls baseline alignment between value and unit text.

### BitColumns

Defines the number of columns used for bit display.

### ChoiceFontSize / ChoiceHeight

Affects bool and bit choice presentation.

### InlineCaptionText / InlineCaptionVisible

Controls optional inline captions.

### BoolChoiceWidth / BoolChoiceHorizontalAlignment

Affects bool choice layout.

## Functions and Behavior

### Render typed property presentation

The component formats values, units, and labels consistently for shared widget usage.

### Emit bit choice events

Bit selections are emitted through `BitChoiceClicked`.

### Emit bool choice events

Bool selections are emitted through `BoolChoiceClicked`.

## Runtime Notes

This component is used by item and signal widgets and is part of their display and input pipeline.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/Property.md`
- Help file: `src/HornetStudio/docs/widgets/help/Property.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/Property/PropertyControl.axaml.cs`
