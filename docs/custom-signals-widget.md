# CustomSignals Widget

The `CustomSignals` widget provides a low-code way to publish project-local signals without requiring Python.

## Use Cases

- User input values such as numbers, booleans, or text.
- Constant project values.
- Simple computed values derived from existing registry targets.

## Signal Modes

- `Input`: writable runtime value intended for direct user interaction.
- `Constant`: fixed value defined in the layout.
- `Computed`: value derived from one or more source paths.

## Supported Computed Operations

- `Copy`
- `Add`
- `Subtract`
- `Multiply`
- `Divide`
- `Min`
- `Max`
- `GreaterThan`
- `LessThan`
- `Equals`
- `And`
- `Or`
- `Concat`
- `If`

## Target Paths

Persisted target paths use dot notation and are stored relative to the current folder when possible:

`CustomSignals.<SignalName>`

Across folders or for absolute references this becomes:

`studio.<Folder>.CustomSignals.<SignalName>`

These paths can be consumed by existing signal widgets, charts, loggers, and other runtime features.

## Persistence

In `Folder.yaml`, custom signal definitions are stored under the widget `Properties.CustomSignals` as a list of structured entries.

## When To Use Python Instead

Use Python when you need:

- external communication
- file or network access
- complex multi-step calculations
- custom runtime workflows that exceed the built-in operation set
