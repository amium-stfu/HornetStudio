# Functions Help

## Widget Type

`Functions`

## Overview

The Function Browser visualizes the central function registry for one folder. Admin-level users open it from the lower `Browser` section in the right folder sidebar by selecting `Functions`. It currently discovers declarative YAML function definitions below the current folder-local `Scripts/Functions/` directory, still reads legacy files below `Scripts/Workflows/` during transition, and also shows read-only Python entries from Applications and Python runtime metadata. Each row is backed by a registry entry that carries function kind, source, capabilities, and current validity. Function availability does not depend on placing a widget. The catalog body uses compact rows with a scrollable list area so the header actions and directory text remain stable while many functions are present. Older folders can still render a placed `Functions` widget, but that path is now legacy compatibility only.

## Properties

### Name / Path / FolderName

These identity values belong to the widget itself inside `Folder.yaml`. They do not contain the function steps.

### Function File Location

New function definitions are stored as separate YAML files below:

`Scripts/Functions/<function-name>.yaml`

Legacy function files below `Scripts/Workflows/` are still discovered when no same-named file exists in `Scripts/Functions/`.

## Catalog Model

### Function Definitions

Function definitions are the persisted YAML documents that contain `name` and `steps`.

### Function Registry Entries

Function registry entries are lookup and display records used by the widget. They include a stable reference, name, kind, source, capabilities, and current validity state.

### Function Calls

Buttons can now reference runnable registry entries through `RunFunction` without changing where declarative YAML files are owned. Workflow callers and future monitor integrations can use the same stable references later. The picker shows friendly labels, but persistence stays on stable references. New YAML selections use `yaml:<name>`, while existing `declarative:<name>` values still resolve to the same YAML entry. Python registry entries stay read-only in `Functions`, but registered Python entries are now executed through `RunFunction` by resolving the same stable registry reference.

## Browser Actions

### Add Function

- Opens the function editor dialog
- Validates the function display name and file name before save
- Creates one YAML file only after the dialog is saved successfully

### Run / Stop

- Uses one row action button instead of showing separate `Run` and `Stop` buttons side by side
- Shows `Run` for runnable and valid entries that are not currently running
- Shows `Stop` only for the matching running declarative function entry
- Requests a controlled stop through the existing `RunFunction` execution path
- Changes the compact state from `Running` to `Stopping` until the execution finishes
- Stays available for Python entries only while the matching runtime target is currently registered; Python rows do not expose `Stop`

### Edit

- Loads the selected function file into the function editor dialog
- Saves changes back to the same YAML file
- Supports editing nested `IfThenElse` and `While` steps including compact branch/body step lists and a dedicated Condition dialog for formula and variable editing
- Renders editable `Log`, `SetValue`, and `Delay` steps as compact inline rows without duplicate summary text
- Is only available for declarative registry entries that allow editing

### Delete

- Opens a confirmation dialog before removing the selected YAML file
- Refreshes only the current function list after deletion
- Is only available for declarative registry entries that allow deletion

## Catalog Row Layout

- Each row shows `[Type] [Name + Source] [Status] [Run/Stop] [Edit] [Delete]`
- Declarative entries are displayed as `YAML`; Python entries are displayed as `Python`
- `YAML` and `Python` use the same type badge shape and visual weight
- Status is quiet text rather than a large badge
- Long names and source details are trimmed to one line and remain available through the row tooltip
- Invalid entries stay visibly marked through the compact `Invalid` state and expose their validation message in the tooltip
- Only the catalog list area scrolls

## YAML Schema

### Root fields

- `name`: required display name of the function
- `steps`: required ordered step list

### Supported step types

#### Log

- `type: Log`
- `targetLog`: required explicit log target
- `level`: optional `Debug`, `Info`, `Warning`, `Error`, or `Fatal`
- `text`: required log message

#### SetValue

- `type: SetValue`
- `target`: required target item path
- `value`: optional literal scalar value written to the target
- `valueFrom`: optional source item path; when configured, the current value of that item is read at runtime and written to the target instead of `value`
- When both `value` and `valueFrom` are absent, an empty string is written

#### Delay

- `type: Delay`
- `milliseconds`: required non-negative integer

#### IfThenElse

- `type: IfThenElse`
- `condition`: required boolean expression
- `variables`: optional list of step-local condition variables with `name` and `sourcePath`
- `then`: required step sequence
- `else`: optional step sequence

#### While

- `type: While`
- `condition`: required boolean expression
- `variables`: optional list of step-local condition variables with `name` and `sourcePath`
- `steps`: required loop body sequence
- The loop body must contain at least one positive `Delay` step as a guard against busy loops

#### StopFunction

- `type: StopFunction`
- Ends the current function execution with the final state `Done`

## Editor Scope

- The editor can add, remove, and reorder `Log`, `SetValue`, `Delay`, `IfThenElse`, `While`, and `StopFunction` steps.
- The editor uses target browsing for `SetValue.target` and source item browsing for `SetValue.valueFrom`.
- The editor uses discovered process log targets for `Log.targetLog`.
- `Log`, `SetValue`, and `Delay` keep their editable controls in one compact row so nested branches stay shorter and easier to scan.
- `IfThenElse` and `While` keep the main step row compact and open a dedicated `Condition` dialog for formula and variable editing.
- The `Condition` dialog uses the shared boolean condition editor with variable rows, source picking, token buttons, and live validation.
- Each `IfThenElse` branch can contain nested declarative steps.
- Each `While` body can contain nested declarative steps.
- `If Block`, `Else Block`, and `While Body` show the same compact step rows with subtle nesting guides instead of repeating summary/detail layouts.
- New `While` rows start with a default `Delay` of `100` ms. The required positive delay may be moved within the loop body, but the editor and validation reject a `While` body without any positive `Delay` guard.

## Current Widget Behavior

- The primary user entry point is the Admin-only right sidebar `Browser` -> `Functions` entry, which opens the dedicated browser window
- Queries the central registry for discovered function entries
- The declarative registry provider discovers `.yaml` and `.yml` files below `Scripts/Functions`
- The declarative registry provider also reads legacy files below `Scripts/Workflows`
- The Python registry provider reads registered runtime target paths and function names from existing Python runtime metadata
- Builds YAML registry entries with stable references such as `yaml:<file-name>` while continuing to resolve legacy `declarative:<file-name>` references
- Builds Python registry entries with stable references such as `python:<target-path>:<function-name>`
- Parses and validates each declarative file using the function YAML codec
- Shows each row as a compact catalog entry with type badge, trimmed name/source text, quiet compact state, and row actions
- Shows declarative entries as `YAML` and Python entries as `Python` using consistent type badges
- Shows `Ready` for valid entries, `Invalid` for invalid entries, `Running` while a row-level execution is active, and `Stopping` after a stop request was sent
- Updates the same row state when a function is started or stopped through Button Interaction Rules
- Exposes source, file, reference, and validation details through the row tooltip
- Shows Python rows as read-only entries without `Edit` or `Delete`
- Exposes runnable Python rows to `RunFunction` when the target client and function metadata are available
- Exposes one row-level `Run`/`Stop` action that toggles to `Stop` only for the matching running declarative execution
- Opens a dense function editor dialog for `Add Function` and `Edit`
- Keeps `IfThenElse` and `While` rows compact in that editor by showing condition summary plus branch or body counts
- Opens the full condition surface only from the `Add Condition` or `Condition` button
- Validates `While` loop bodies so they always contain a positive `Delay` guard
- Treats `StopFunction` as a controlled `Done` completion instead of a failure
- Saves new functions only after dialog validation succeeds
- Deletes selected function files only after confirmation
- Updates the widget footer with the number of discovered registry entries
- Refreshes discovery manually through the widget `Refresh` button
- Does not scan Python files directly and does not change Python runtime registration

## Legacy Widget Compatibility

- Existing `Folder.yaml` files that already contain `ControlKind.Functions` continue to load.
- The legacy widget hosts the same catalog/editor surface as the folder-level browser.
- The Browser section itself is visible only for Admin-level users.
- New widget selection no longer offers `Functions` as a normal canvas widget.
- The browser is the preferred workflow for adding, editing, deleting, refreshing, running, and stopping functions.

## Execution Foundation

The repository already contains a function executor with sequential step handling, cancellation, condition branching, loops, and clear `Done`, `Failed`, and `Canceled` outcomes. `While` conditions are evaluated before every iteration, each loop body must contain a positive `Delay` guard, and `StopFunction` exits the current function as `Done` even from inside nested `IfThenElse` or `While` blocks. Buttons now use that execution foundation through `RunFunction` for declarative registry entries, and the Functions widget row-level `Run`/`Stop` toggle reuses the same execution and controlled-stop path instead of introducing a separate executor. Python registry entries come from existing runtime metadata instead of direct file scanning, remain available even without a `Functions` widget, and are dispatched through the existing Python client runtime when selected from `RunFunction`. The picker shortens technical Python references into concise labels for display only. The optional interaction `Argument` is forwarded to Python using the existing JSON-or-plain-text payload behavior and is currently ignored by declarative functions.

## Example YAML

```yaml
name: startup_sequence
steps:
  - type: Log
    targetLog: Logs.process
    level: Info
    text: Starting sequence
  - type: SetValue
    target: studio.main.pump.enable
    value: true
  - type: IfThenElse
    condition: "{Temperature} > 20"
    variables:
      - name: Temperature
        sourcePath: custom_signals_1.temperature
    then:
      - type: Delay
        milliseconds: 500
      - type: Log
        targetLog: Logs.audit
        text: Warm start completed
    else:
      - type: Log
        targetLog: Logs.audit
        text: Warm start skipped
  - type: While
    condition: "{Enabled} == true"
    variables:
      - name: Enabled
        sourcePath: custom_signals_1.enabled
    steps:
      - type: Delay
        milliseconds: 100
      - type: StopFunction
```

## Help Notes for Users

- Keep function files small and declarative.
- Treat the Function Browser as a registry viewer and declarative editor, not as the owner of all function implementations.
- Use step-local `variables` on `IfThenElse` when the condition should read runtime sources directly.
- Use step-local `variables` on `While` when the loop condition should read runtime sources directly.
- Use the `Condition` dialog `+` button to add step-local variables before inserting them into the formula.
- Use explicit `targetLog` values for every `Log` step.
- Keep one positive `Delay` step inside every `While` body to avoid hot loops.
- Keep conditions boolean and focused.
- Store declarative function content only in YAML files, not inside `Folder.yaml`.
- Keep Python functions in Applications and Python infrastructure; they are only referenced through the registry.

## Suggested Help Window Metadata

- Summary file: `src/HornetStudio/docs/widgets/Functions.md`
- Help file: `src/HornetStudio/docs/widgets/help/Functions.help.md`

## Source

- `src/HornetStudio.Editor/Widgets/Workflow/WorkflowControl.axaml`
- `src/HornetStudio.Editor/Widgets/Workflow/WorkflowControl.axaml.cs`
- `src/HornetStudio.Editor/Widgets/Workflow/FunctionBrowserDialogWindow.axaml`
- `src/HornetStudio.Editor/Widgets/Workflow/FunctionBrowserDialogWindow.axaml.cs`
- `src/HornetStudio.Editor/Widgets/Workflow/WorkflowDefinitionCodec.cs`
- `src/HornetStudio.Editor/Widgets/Workflow/WorkflowExecutor.cs`
