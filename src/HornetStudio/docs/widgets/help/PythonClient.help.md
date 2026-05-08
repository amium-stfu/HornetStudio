# PythonClient Help

## Widget Type

`PythonClient`

## Overview

The PythonClient widget hosts a Python client runtime inside the page and presents start, stop, and runtime state information.

## Properties

### PythonScriptPath

Defines the configured Python client script path.

### RuntimeStatusText

Current runtime state label shown in the UI.

### RuntimeDetailText

Additional status or detail text for the runtime.

### CanToggleRuntime

Controls whether the runtime toggle action is currently available.

### RuntimeToggleText

Caption for the start or stop action.

### RuntimeStatusBackground / RuntimeStatusForeground

Visual status brushes for the runtime state presentation.

## Functions and Behavior

### Start runtime

Starts the configured Python client runtime.

### Stop runtime

Stops the running runtime instance and cleans up state.

### Refresh presentation

The widget recalculates its visible status based on current runtime information.

### Observe item changes

Item changes can affect the configured script path and status presentation.

## Runtime Notes

The widget manages lifecycle state transitions such as stopped, running, stopping, and error.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/PythonClient.md`
- Help file: `src/HornetStudio/docs/widgets/help/PythonClient.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/PythonClient/PythonClientControl.axaml.cs`