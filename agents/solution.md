# HornetStudio Solution Rules

## Solution Scope

- These rules are specific to the HornetStudio repository and complement the general modules under `/agents`.
- Keep changes aligned with the existing solution structure, existing documentation layout, and repository conventions.

## Verified Build Conventions

- The primary solution build target is `HornetStudio.sln`.
- Restore and build HornetStudio directly against the configured internal `amium-at` NuGet feed.
- Prefer `dotnet build HornetStudio.sln --no-restore` once restore has already completed.
- Do not replace solution-level verification with project-only builds when the requested scope is solution-wide.
- Focused verification for `src/HornetStudio.Host.Tests` should run the built DLL directly with `dotnet <path-to-dll>` because it is a console self-test (`OutputType=Exe`), not a `Microsoft.NET.Test.Sdk` test project.

## Python Integration

- When changing or extending the bundled Python helper API under `Host/Python/**` or `UiEditor/Templates/ui_python_client/**`, update the command documentation in `Host/Python/Integration/ui-python-client-commands.md` and `UiEditor/Templates/ui_python_client/COMMANDS.md` in the same change.
- Treat `Host/Python/Integration/ui-python-client-commands.md` as the source-of-truth documentation for predefined Python client commands.
- When changing Python bridge behavior, generated Python folder contents, template workflow, or Python interaction argument handling, update `Host/Python/Integration/python-system-overview.md` and `UiEditor/Templates/PYTHON_SYSTEM.md` in the same change.
- When working on Python templates, Python environments, or generated Python scripts, consult `Host/Python/Integration/python-system-overview.md` and `Host/Python/Integration/ui-python-client-commands.md` first.

## Widget System

- When changing widget code under `UiEditor/Widgets/**`, update the matching widget Markdown documentation under `HornetStudio/docs/widgets/` in the same change. Use one Markdown file per widget type and keep the file name aligned with the persisted widget `Type` value so the documentation can be loaded later inside the application.
- For widget selection, use shorter separate description files instead of full `.md` help files, as the complete Markdown help can appear overloaded.
- When changing widget code under `UiEditor/Widgets/**`, update the matching short selection description file under `HornetStudio/docs/widgets/descriptions/` in the same change. Use one Markdown file per widget type and keep the file name aligned with the persisted widget `Type` value so the picker can load the concise description later.
- When changing widget code under `UiEditor/Widgets/**`, update the matching detailed help file under `HornetStudio/docs/widgets/help/` in the same change. Use one help Markdown file per widget type and keep the file name aligned with the persisted widget `Type` using the pattern `<Type>.help.md` so the help content can later be loaded inside a help window.
- Remove mixed target/property patterns in widgets: controls should register all required control items, including colors, for external access, and redundant methods should be removed.

## UI and Theme

- Align new UI elements with the theme guidelines and ensure that new UI areas are designed to match the existing windows and dialogs of the project.
- Ensure that new icons are always colored according to the theme and do not use fixed colors.
- For computed formulas, ensure that variable and function buttons are user-friendly and easily accessible.

## Repository Conventions

- Prefer a minimally fragmented architecture: encapsulate and provide functions within their respective components rather than requiring additional separate helper components or intermediary signals.
- Prioritize clear, consistent architectural rules over mixed patterns for easier documentation later.
- For the planned logger file splitting, ensure that a new file is rotated at the configured time, even if the current file is nearly empty.
- Remove all instances of `FilteredSignals`, as it is deprecated legacy code. Use `EnhancedSignal` as the replacement.
