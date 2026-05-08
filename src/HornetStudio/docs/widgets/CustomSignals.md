# CustomSignals Widget

## Type

`CustomSignals`

## Purpose

Provides a low-code way to define project-local signals without requiring Python.

## Typical Use Cases

- User input values
- Constant project values
- Simple computed values derived from registry targets

## Key Configuration

- Signal definitions stored in `Properties.CustomSignals`
- Signal mode such as input, constant, or computed
- Trigger mode for computed signals
- Formula variables and operations
- Optional write routing using `IsWritable`, `WriteMode`, and `WritePath`

## Runtime Notes

Persisted target paths use dot notation and are stored relative to the current folder when possible.
Published custom signals use the widget identity in their registry path: `studio.{FolderName}.{WidgetName}.{SignalName}`.
Input signals can advertise a separate direct or request-based write target so generic editors and interactions do not need to know the backend-specific path.

## Source

- `src/Hornetstudio.Editor/Widgets/CustomSignals/`
- `src/Hornetstudio.Editor/Widgets/CustomSignals/CustomSignalsControl.axaml.cs`
- `src/Hornetstudio.Editor/Widgets/CustomSignals/CustomSignalEditorDialogWindow.axaml`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/CustomSignals.help.md`