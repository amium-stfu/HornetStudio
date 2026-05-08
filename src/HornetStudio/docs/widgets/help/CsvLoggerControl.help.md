# CsvLoggerControl Help

## Widget Type

`CsvLoggerControl`

## Overview

The CsvLoggerControl widget records configured signal values into CSV files and publishes logger runtime information.

## Properties

### CsvDirectory

Base directory used for logger output.

### CsvFilename

File name used for the logger output.

### CsvAddTimestamp

Controls whether timestamps are added to output rows or filenames according to the runtime logger implementation.

### CsvIntervalMs

Defines the logging interval in milliseconds.

### CsvSignalPaths

Stores the list of signal paths to record.

### CsvSplitDaily

Enables time-based file rotation.

### CsvSplitDailyTime

Defines the configured split time.

### CsvSplitMaxFileSizeMb

Defines an optional size-based split threshold.

### CsvPersistenceMode

Defines how aggressively data is flushed or buffered.

### CsvFlushIntervalMs / CsvFlushBatchSize

Advanced flush tuning values for runtime logging.

## Functions and Behavior

### Update runtime snapshot

Publishes runtime values such as recording state, output path, last file, and status.

### Track item property changes

Changes to output-related properties trigger runtime snapshot updates.

### React to registry item changes

External state changes can be reflected back into the UI and runtime view.

## Runtime Notes

The widget exposes runtime sub-items that can later be inspected or shown in help and monitoring surfaces.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/CsvLoggerControl.md`
- Help file: `src/HornetStudio/docs/widgets/help/CsvLoggerControl.help.md`

## Source

- `src/Hornetstudio.Editor/Widgets/CsvLogger/EditorCsvLoggerControl.axaml.cs`
- `src/Hornetstudio.Editor/Models/PageItemModel.cs`