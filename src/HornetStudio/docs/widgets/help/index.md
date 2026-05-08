# Widget Help Index

This folder contains detailed help pages for widget types and widget-related editor components.

## Folder Contract

- Location: `src/HornetStudio/docs/widgets/help/`
- One detailed help file per widget type or documented widget-related component
- File name pattern for persisted widget types: `<Type>.help.md`
- Example lookup for a help window: `src/HornetStudio/docs/widgets/help/<Type>.help.md`

## Recommended Viewer Metadata

A later help window can resolve content using:

- `Type`: persisted widget type, for example `Signal`
- `SummaryPath`: `src/HornetStudio/docs/widgets/<Type>.md`
- `HelpPath`: `src/HornetStudio/docs/widgets/help/<Type>.help.md`

## Available Help Files

- `ApplicationExplorer.help.md`
- `Button.help.md`
- `CameraControl.help.md`
- `ChartControl.help.md`
- `CircleDisplay.help.md`
- `CsvLoggerControl.help.md`
- `CustomSignals.help.md`
- `EnhancedSignals.help.md`
- `FolderEditor.help.md`
- `Item.help.md`
- `WidgetList.help.md`
- `LogControl.help.md`
- `Property.help.md`
- `PythonClient.help.md`
- `Signal.help.md`
- `SqlLoggerControl.help.md`
- `TableControl.help.md`
- `UdlClientControl.help.md`
- `ValueInput.help.md`

The split between summary and help files is intended to keep overview pages short while still supporting a later in-app help window with richer property and function details.