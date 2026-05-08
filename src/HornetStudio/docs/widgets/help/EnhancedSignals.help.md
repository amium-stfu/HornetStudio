# EnhancedSignals Help

## Widget Type

`EnhancedSignals`

## Overview

The EnhancedSignals widget manages structured signal definitions and publishes corresponding runtime signals. It replaces older legacy approaches based on filtered signals.

## Properties

### EnhancedSignalDefinitions

Stores the configured enhanced signal definitions.

### Name / Path / FolderName

Changes to identity and path context can trigger runtime rebuilds.

### EffectiveBodyBackground / EffectiveBodyBorder / EffectiveBodyForeground / EffectiveMutedForeground

Theme values used to refresh visible signal row styling.

## Functions and Behavior

### Rebuild runtimes

The widget rebuilds its runtime representation when definitions or naming context change.

### Refresh runtimes

The widget can queue runtime refreshes when registry changes arrive from non-UI threads.

### Queue rebuilds

Observed item changes can be queued so rebuilds occur safely on the UI thread.

### React to registry changes

The widget listens for data registry changes and updates its visible signal rows.

### Write routing

Enhanced signal definitions can define `IsWritable`, `WriteMode`, and `WritePath`. Generic UI controls can then write through the published enhanced signal while the runtime routes the value to its configured backend path or request channel.

## Migration Note

Use EnhancedSignals instead of deprecated FilteredSignals patterns.
For request-oriented backends, prefer `Request` mode so the published signal stays simple while the actual write uses the runtime request path.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/EnhancedSignals.md`
- Help file: `src/HornetStudio/docs/widgets/help/EnhancedSignals.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/EnhancedSignals/EnhancedSignalsControl.axaml.cs`