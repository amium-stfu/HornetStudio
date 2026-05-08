# Item Widget

## Type

`Item`

## Purpose

Displays and optionally edits a bound target value with caption, body, and footer presentation.

## Typical Use Cases

- Show a runtime value
- Display units and formatting
- Open an input editor for writable targets

## Key Configuration

- Target path
- Target property path and format
- Unit and body caption
- Header, body, and footer styling
- Interaction rules

## Runtime Notes

The item widget resolves a registry target, formats its property view, and can write values back when the target is writable.

## Source

- `src/Hornetstudio.Editor/Models/PageItemModel.cs`
- `src/Hornetstudio.Editor/Widgets/Property/`
- `src/Hornetstudio.Editor/Widgets/ValueInput/`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/Item.help.md`