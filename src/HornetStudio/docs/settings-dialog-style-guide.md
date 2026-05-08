# Settings Dialog Style Guide

This document defines the standard visual and structural pattern for all new settings dialogs in the UiEditor.

## Rule

All new settings dialogs must follow the same style as the widget settings dialog implemented by `EditorPropertyDialog`.

## Required Structure

- Use the dialog shell pattern with a single bordered container, rounded corners, and theme-derived brushes.
- Use top tabs styled like the widget settings tabs. A dialog may expose only one tab when no second category is needed, but the tab visual style must remain the same.
- Organize content into collapsible sections with a left toggle glyph and a bold section title.
- Expand only the sections that are currently active or immediately relevant when the dialog opens.
- Keep inactive optional sections collapsed by default.
- Hide parameters that are not active in the current configuration instead of showing disabled noise.

## Required Theming

- Use theme-derived values from `MainWindowViewModel` for dialog background, borders, text, tabs, section headers, section content, buttons, and input fields.
- Do not introduce hard-coded default colors for normal idle states beyond the existing theme fallback values already used by the shared dialogs.
- Input fields must use the same hover and focus treatment as the widget settings dialog.

## Section Behavior

- Section headers should use the same visual rhythm as the widget settings dialog.
- Use explicit toggle glyphs like `▼` and `▶` or the shared section-toggle pattern.
- Only show the fields that are effective for the current mode or enabled state.
- If a feature is disabled, keep only its primary enable switch visible inside the collapsed or collapsible section.

## Footer Behavior

- Place primary actions in the footer aligned to the right.
- Use the shared dialog button styling for `Save`, `Apply`, `Cancel`, or equivalent actions.

## Implementation Guidance

- Prefer reusing the styles and layout patterns from `src/Hornetstudio.Editor/Widgets/Common/EditorPropertyDialog.axaml`.
- If a dialog needs custom fields, extend the content inside the shared visual pattern rather than inventing a new shell.
- When a new settings dialog is added, verify that it matches the widget settings dialog in tab style, section toggles, spacing, field styling, and footer actions.