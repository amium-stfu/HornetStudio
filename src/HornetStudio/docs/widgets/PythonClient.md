# PythonClient Widget

## Type

`PythonClient`

## Purpose

Hosts a Python client runtime inside the page and exposes start, stop, and status presentation.

## Typical Use Cases

- Run a bundled Python client script
- Show runtime state of Python-backed helpers
- Integrate Python-driven project functionality

## Key Configuration

- Python script path
- Runtime start and stop behavior
- Widget title and status area

## Runtime Notes

The widget tracks runtime state, exposes status text, and updates its presentation when the observed item changes.

## Source

- `src/Hornetstudio.Editor/Widgets/PythonClient/`
- `src/Hornetstudio.Editor/Widgets/PythonClient/PythonClientControl.axaml.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/PythonClient.help.md`