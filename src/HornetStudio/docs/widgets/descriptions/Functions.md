Functions

Legacy compatibility surface for the Admin-only Function Browser. New folders should use the right sidebar `Browser` -> `Functions` entry instead of placing this widget on the canvas.

- Opens the same compact function catalog used by the folder-level browser
- Adds, edits, and deletes declarative YAML functions from the widget UI
- Runs runnable entries and reflects matching declarative executions started from widget rows or Button Interaction Rules
- Validates YAML structure and marks invalid declarative entries
- Keeps function definitions outside `Folder.yaml`
- Supports declarative steps: `Log`, `SetValue`, `Delay`, `IfThenElse`, `While`, and `StopFunction`
- Edits declarative steps in compact inline rows, including nested `IfThenElse` and `While` lists, a dedicated condition dialog, and a required positive Delay guard inside every While body

Best for:
Opening older folders that already contain a persisted `Functions` widget while keeping the Function Browser as the primary workflow.
