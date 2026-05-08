# SqlLoggerControl Help

## Widget Type

`SqlLoggerControl`

## Overview

The SqlLoggerControl widget records configured signal values into a SQLite-backed database file and publishes logging runtime information.

## Properties

### CsvDirectory / CsvFilename

Used together to derive the logger output path.

### CsvIntervalMs

Defines the logging interval.

### CsvSignalPaths

Defines which signals should be recorded.

### CsvPersistenceMode

Controls runtime persistence behavior.

### Name / Path / FolderName

Influence runtime path generation.

## Functions and Behavior

### Update runtime snapshot

Publishes runtime information for monitoring and UI display.

### Track property changes

Output path and identity changes can trigger runtime updates.

### React to registry changes

External state updates can be synchronized into the widget state.

## Runtime Notes

Although the runtime sink is database-based, the widget shares a similar runtime status pattern with the CSV logger.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/SqlLoggerControl.md`
- Help file: `src/HornetStudio/docs/widgets/help/SqlLoggerControl.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/SqlLogger/EditorSqlLoggerControl.axaml.cs`
- `src/Hornetstudio.Editor/Models/PageItemModel.cs`