# Workspace Instructions

These instructions are repository-wide rules for GitHub Copilot in Visual Studio Code and Visual Studio. Apply them for all code generation, edits, refactorings, documentation updates, and file creation in this workspace.

**Note**: The primary workspace-wide agent behavior, workflow, and mode rules are defined in `AGENTS.md` at the repository root. This file provides Copilot-specific execution context and project-specific domain rules that extend or refine the general rules from `AGENTS.md`.

## Copilot Behavior

- Interpret these instructions as persistent workspace rules, even if a task only references a single file.
- Prefer precise edits in existing files over broad rewrites.
- If multiple rules apply, follow the most specific rule for the affected area.
- For larger implementation work, use a new chat to reduce context drift and hallucinations, ensuring token-efficient interactions.

## Project-Specific Domain Rules

### Python Integration

- When changing or extending the bundled Python helper API under `Host/Python/**` or `UiEditor/Templates/ui_python_client/**`, update the command documentation in `Host/Python/Integration/ui-python-client-commands.md` and `UiEditor/Templates/ui_python_client/COMMANDS.md` in the same change.
- Treat `Host/Python/Integration/ui-python-client-commands.md` as the source-of-truth documentation for predefined Python client commands.
- When changing Python bridge behavior, generated Python folder contents, template workflow, or Python interaction argument handling, update `Host/Python/Integration/python-system-overview.md` and `UiEditor/Templates/PYTHON_SYSTEM.md` in the same change.
- When working on Python templates, Python environments, or generated Python scripts, consult `Host/Python/Integration/python-system-overview.md` and `Host/Python/Integration/ui-python-client-commands.md` first.

### Widget System

- When changing widget code under `UiEditor/Widgets/**`, update the matching widget Markdown documentation under `HornetStudio/docs/widgets/` in the same change. Use one Markdown file per widget type and keep the file name aligned with the persisted widget `Type` value so the documentation can be loaded later inside the application.
- For widget selection, use shorter separate description files instead of full .md help files, as the complete Markdown help can appear overloaded.
- When changing widget code under `UiEditor/Widgets/**`, update the matching short selection description file under `HornetStudio/docs/widgets/descriptions/` in the same change. Use one Markdown file per widget type and keep the file name aligned with the persisted widget `Type` value so the picker can load the concise description later.
- When changing widget code under `UiEditor/Widgets/**`, update the matching detailed help file under `HornetStudio/docs/widgets/help/` in the same change. Use one help Markdown file per widget type and keep the file name aligned with the persisted widget `Type` using the pattern `<Type>.help.md` so the help content can later be loaded inside a help window.
- Remove mixed target/property patterns in widgets: controls should register all required control items, including colors, for external access, and redundant methods should be removed.

### UI and Theme

- Align new UI elements with the theme guidelines and ensure that new UI areas are designed to match the existing windows/dialogs of the project. Ensure that new icons are always colored according to the theme and do not use fixed colors.
- For computed formulas, ensure that variable and function buttons are user-friendly and easily accessible.

### Architecture

- Prefer a minimally fragmented architecture: encapsulate and provide functions within their respective components rather than requiring additional separate helper components or intermediary signals. Prioritize clear, consistent architectural rules over mixed patterns for easier documentation later.

## Component-Specific Rules

### Logging

- For the planned logger file splitting, ensure that a new file is rotated at the configured time, even if the current file is nearly empty.

### Signals

- Remove all instances of `FilteredSignals`, as it is deprecated legacy code. Use `EnhancedSignal` as the replacement.