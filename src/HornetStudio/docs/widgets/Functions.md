# Functions

## Type

`Functions`

## Purpose

Provides the Admin-only Function Browser workflow for callable functions. The browser is opened from the `Browser` section in the right folder sidebar, visualizes the central folder-local function registry, contributes declarative YAML entries from `Scripts/Functions/`, still reads legacy `Scripts/Workflows/` files during transition, and also shows read-only Python function entries from runtime/application metadata without taking ownership of Python files or execution. A placed `Functions` widget remains loadable only for legacy `Folder.yaml` compatibility.

## Typical Use Cases

- Open the folder-local Function Browser from the Admin-only sidebar browser without placing a widget on the canvas
- Show which functions are available in one folder
- Inspect one central lookup surface for future Buttons, Workflows, and Monitor actions
- Create new function YAML files from the widget UI
- Edit flat function steps without opening the filesystem manually
- Delete obsolete function YAML files with confirmation
- Validate function YAML files directly from the widget surface
- Surface invalid function files with a clear error state
- Keep function content outside `Folder.yaml`
- Keep new page composition free of a dedicated `Functions` widget

## Key Configuration

- New function files live below `Scripts/Functions/` next to the current folder `Folder.yaml`
- Legacy files below `Scripts/Workflows/` are still discovered during transition
- The widget list is backed by widget-independent function registry entries, not by raw file enumeration in the UI layer
- Each registry entry distinguishes function definition content from lookup metadata such as kind, source, and capabilities
- Current registry kind support includes `Declarative` and read-only `Python`; the widget displays declarative entries as `YAML`, while Python entries stay `Python`
- The widget itself stays small and UI-focused inside `Folder.yaml`
- Supported function YAML root fields are `name` and `steps`
- Supported declarative step types are `Log`, `SetValue`, `Delay`, `IfThenElse`, `While`, and `StopFunction`
- Widget header actions are `Add Function` and `Refresh`
- The primary entry point is the Admin-only `Browser` -> `Functions` entry in the right sidebar, not the widget picker
- Catalog rows use a compact layout with a type badge, name/source text, quiet state text, one `Run`/`Stop` toggle action, and optional `Edit`/`Delete`
- Both YAML and Python rows use the same type badge shape; declarative entries display as `YAML`
- Declarative YAML rows expose `Run` when idle, `Stop` while the matching function is running, and `Edit`/`Delete` when supported; Python rows can expose `Run` when currently registered and stay read-only for editing
- Only the catalog list body scrolls; the header actions and directory text stay stable
- Function availability does not depend on placing a `Functions` widget on the page
- New widget selection does not offer `Functions`; only older persisted `ControlKind.Functions` entries still render
- `Log` requires `targetLog`, optional `level`, and `text`
- `SetValue` requires `target` and either a literal `value` or a `valueFrom` source item path; when `valueFrom` is configured the current value of that item is read at runtime and written to the target
- `Delay` requires `milliseconds`
- `IfThenElse` requires `condition` and `then`; `else` is optional and `variables` can define step-local condition sources
- `While` requires `condition` and `steps`; `variables` are optional and each `While` body must contain at least one positive `Delay` step as a loop guard
- `StopFunction` ends the current function execution with `Done`
- The editor can add, reorder, and remove `Log`, `SetValue`, `Delay`, `IfThenElse`, `While`, and `StopFunction` steps
- `Log`, `SetValue`, and `Delay` rows use one compact editable row instead of showing a duplicate summary above separate fields
- `IfThenElse` and `While` stay compact in the function step list and edit their condition through a dedicated Condition dialog that uses the shared boolean condition editor with variable rows, source picking, token buttons, and live validation

## Editor Notes

- Open the browser from the right sidebar `Browser` -> `Functions` entry as an Admin-level user
- `Add Function` opens a function editor dialog instead of creating an empty file immediately
- New function file names are validated as `snake_case` and saved as `.yaml`
- Editing saves back to the selected file
- Nested `If Block`, `Else Block`, and `While Body` sections reuse the same compact step row pattern with light visual nesting guides
- New `While` rows start with a default `Delay` of `100` ms; that required positive delay may be moved within the loop body but the editor keeps one positive guard delay in the body
- Deleting removes only the selected function file

## Runtime Notes

The current Function Browser implementation visualizes the central function registry in compact rows. Admin-level users open it from the lower `Browser` section of the right folder sidebar, while non-Admin users do not see that management surface. Each row shows a consistent type badge for `YAML` or `Python`, trimmed function name/source text, quiet `Ready`/`Invalid`/`Running`/`Stopping` state text, one execution action that toggles between `Run` and `Stop`, and optional `Edit`/`Delete` actions. Declarative entries are displayed as `YAML`, invalid entries stay visibly marked and expose the validation error through the row tooltip, and only the catalog list body scrolls so the header actions and directory text remain stable. The row state listens to catalog execution changes, so functions started or stopped through Button Interaction Rules update the same `Running`/`Stopping` display. The browser still validates declarative YAML files and exposes nested step editing for declarative function content including editable `IfThenElse` and `While` branches. In the Function Editor, editable `Log`, `SetValue`, and `Delay` steps now render as compact inline rows with their active controls directly in the step header, which removes redundant summary text and makes branch lists shorter. `IfThenElse` and `While` rows stay compact, show a condition summary with branch counts or body counts, and open a dedicated Condition dialog for formula and variable editing instead of embedding the full editor inline. `While` bodies require at least one positive `Delay` guard to avoid busy loops, and `StopFunction` ends the current declarative function with `Done` even when used inside nested control-flow steps. Buttons can now consume runnable registry entries through `RunFunction` by storing the stable registry reference and resolving it at click time, which confirms that the registry is widget-independent. The interaction-rule picker shows friendly labels for those entries, while persistence stays on stable references such as `yaml:<name>` and existing `declarative:<name>` values remain compatible. Python functions are surfaced as read-only registry entries from runtime registration, are intentionally not scanned from files here, remain available without placing `Functions`, and can be executed through `RunFunction` when currently registered. Legacy placed `Functions` widgets still open the same catalog surface when older folders are loaded.

## Source

- `src/HornetStudio.Editor/Widgets/Workflow/WorkflowControl.axaml`
- `src/HornetStudio.Editor/Widgets/Workflow/WorkflowControl.axaml.cs`
- `src/HornetStudio.Editor/Widgets/Workflow/FunctionBrowserDialogWindow.axaml`
- `src/HornetStudio.Editor/Widgets/Workflow/FunctionBrowserDialogWindow.axaml.cs`
- `src/HornetStudio.Editor/Widgets/Workflow/WorkflowDefinitionCodec.cs`
- `src/HornetStudio.Editor/Widgets/Workflow/WorkflowExecutor.cs`

## Help

- Detailed help: `src/HornetStudio/docs/widgets/help/Functions.help.md`
