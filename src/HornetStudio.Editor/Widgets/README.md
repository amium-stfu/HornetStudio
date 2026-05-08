# UiEditor Widgets

This directory contains all Avalonia views and related classes for the UiEditor widgets. The structure is organized by widget type.

## Common

Shared infrastructure and building blocks that are reused by multiple widgets:

- EditorShellControl.axaml(.cs) – base shell for editor widgets (header/body/chrome)
- EditorTemplateControl.axaml(.cs) – template shell with header/body slots and actions
- EditorContainerControl.cs – helper class for editor containers
- EditorTextInputPad.axaml(.cs), EditorNumericInputPad.axaml(.cs), EditorHexInputPad.axaml(.cs) – on-screen input pads for text, numeric, and hex input
- EditorPropertyDialog.axaml(.cs), EditorPropertyDialogWindow.axaml(.cs) – dialog for editing item/widget properties
- InteractionRulesEditorDialogWindow.axaml(.cs) – dialog for editing interaction rules
- ChartSeriesEditorDialogWindow.axaml(.cs) – dialog for configuring chart series
- ThemeSvgIcon.axaml(.cs) – SVG icon control for themeable icons

## Button

Widget type for buttons:

- EditorButtonControl.axaml(.cs) – presentation and behavior of a button in the editor (including header, actions, click handling)

## Signal

Widget type for individual signals (parameters/status displays, etc.):

- Signal/EditorSignalControl.axaml(.cs) – presentation and interaction logic of a single signal widget

## List

Widget type for lists of items/buttons:

- EditorListControl.axaml(.cs) – list container for multiple signal/button widgets, including selection and layout

## Log

Widget type for process/system logs:

- EditorLogControl.axaml(.cs) – log view with filters, pause/resume, color coding, and clipboard support

## ValueInput

Widget for value input (value editor):

- EditorValueInputControl.axaml(.cs) – overlay/dialog widget for editing property values (text, numeric, hex, bitmask) including on-screen keyboard

## FolderEditor

Widgets related to folder editing in the editor:

- FolderEditorControl.axaml(.cs) – main drawing surface for placing, moving, and scaling widgets in a folder
- CachedFolderHostControl.axaml(.cs) – host that caches one FolderEditorControl per folder and synchronizes visibility

## Property

Visualization of item property values in a widget:

- PropertyControl.axaml(.cs) – display and interaction widget for item properties (text, units, bit/bool choices, etc.)

## RealtimeChart

Widget type for live charts (e.g. measurements):

- RealtimeChartControl.axaml(.cs) – real-time chart based on ScottPlot, including pause functionality, presentation, and interaction

## UdlClient

Widget for connecting the Udl client (CAN/network):

- UdlClientControl.axaml(.cs) – connection widget for the UdlClient (connect/disconnect, status, attached items)

## CustomSignals

Widget for project-local input, constant, and computed signals:

- CustomSignals/CustomSignalsControl.axaml(.cs) – list-based management widget that publishes custom signals into the registry
- CustomSignals/CustomSignalEditorDialogWindow.axaml(.cs) – dialog for creating and editing individual custom signal definitions

## Dialogs

Dialogs used by editor widgets:

- AttachItemsEditorDialogWindow.axaml(.cs) – dialog for selecting/assigning runtime items to a UdlClient widget

---

This layout is chosen so that:

- **Common** contains all reusable building blocks,
- each concrete widget type has its own subfolder,
- aliases like `*Widget` (e.g. EditorButtonWidget) live in the same namespace Hornetstudio.Editor.Widgets and simply inherit from the corresponding `*Control` classes. Legacy `EditorItem*` aliases still point to the signal control.