# ApplicationExplorer Help

## Widget Type

`ApplicationExplorer`

## Overview

The ApplicationExplorer widget manages configured application environments inside the current folder context. It is intended for scenarios where local or project-bound helper applications need to be started, stopped, monitored, and addressed by interaction rules.

## Properties

### ApplicationDefinitions

Stores the configured application entries for the widget. The widget rebuilds its environment list from this value.

### ApplicationAutoStart

Controls whether configured environments should be started automatically when the widget becomes active.

### Name

Used as part of the widget identity and path generation.

### Header / Body / Footer styling

The common widget appearance properties control how the widget shell is displayed in the editor and runtime.

## Functions and Behavior

### Rebuild environment list

When the item or application definition data changes, the widget rebuilds the visible list of environments.

### Auto-start environments

When enabled, the widget triggers startup logic after attaching to the visual tree.

### Resolve interaction targets

The widget exposes application-related interaction targets that other controls can address.

## Runtime Notes

The widget reacts to item changes and refreshes its environment list from the stored definitions. It also acts as a bridge between UI interactions and Python-backed application runtime handling.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/ApplicationExplorer.md`
- Help file: `src/HornetStudio/docs/widgets/help/ApplicationExplorer.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/ApplicationExplorer/ApplicationExplorerControl.axaml.cs`