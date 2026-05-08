# ApplicationExplorer Widget

## Type

`ApplicationExplorer`

## Purpose

Manages configured application environments inside a folder and exposes runtime actions for start, stop, and interaction handling.

## Typical Use Cases

- Start and stop local helper applications
- Manage project-bound application definitions
- Provide interaction targets for Python-backed runtime features

## Key Configuration

- Application definitions list
- Auto-start behavior
- Header, body, and footer theme settings

## Runtime Notes

The widget rebuilds its environment list from the stored application definitions and can trigger application startup automatically when configured.

## Source

- `src/Hornetstudio.Editor/Widgets/ApplicationExplorer/`
- `src/Hornetstudio.Editor/Widgets/ApplicationExplorer/ApplicationExplorerControl.axaml.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/ApplicationExplorer.help.md`