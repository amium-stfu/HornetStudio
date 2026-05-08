# CustomSignals Help

## Widget Type

`CustomSignals`

## Overview

The CustomSignals widget provides project-local signals without requiring Python. It supports input, constant, and computed signals and can publish them into the runtime registry.

## Properties

### CustomSignalDefinitions

Stores the complete signal definition set for the widget.

### Name / Path / FolderName

These identity properties affect generated registry paths and refresh behavior.

### EffectiveBodyBackground / EffectiveBodyBorder / EffectiveBodyForeground / EffectiveMutedForeground

Theme-related values used to refresh signal row presentation.

## Functions and Behavior

### Rebuild signal rows

The widget parses stored definitions and creates runtime rows for them.

### Publish signals

The widget publishes input, constant, and computed values into the registry.
Published custom signal paths follow the widget-aware format `studio.{FolderName}.{WidgetName}.{SignalName}`.

### Write routing

Input signals can define `IsWritable`, `WriteMode`, and `WritePath` so value editors and interaction rules can write through a friendly signal while the actual target path stays configurable.

### Preserve input values

When rebuilding or refreshing, existing input values can be preserved.

### Manual trigger handling

Computed signals can expose manual trigger paths and delayed evaluations.

### Source-change recomputation

Computed signals can recompute automatically when dependent registry values change.

## Supported Signal Modes

- `Input`
- `Constant`
- `Computed`

## Help Notes for Users

Use this widget when you need lightweight project-local logic. Prefer Python only when external communication or larger workflow logic is required.
Use write routing when the displayed signal should stay user-friendly but the actual write must reach another registry target or a request endpoint.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/CustomSignals.md`
- Help file: `src/HornetStudio/docs/widgets/help/CustomSignals.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/CustomSignals/CustomSignalsControl.axaml.cs`
- `src/Hornetstudio.Editor/Widgets/CustomSignals/CustomSignalEditorDialogWindow.axaml`