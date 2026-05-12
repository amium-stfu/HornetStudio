# Workspace Instructions

These instructions are repository-wide rules for GitHub Copilot in Visual Studio Code and Visual Studio. Apply them for all code generation, edits, refactorings, documentation updates, and file creation in this workspace.

The primary workspace-wide agent behavior, workflow, mode rules, and project-specific rules are defined in the repository root `AGENTS.md` and the modules under `agents/`.

Use this file only as Copilot-specific routing and execution context. Do not treat it as a separate source of truth.

## Copilot Behavior

- Interpret these instructions as persistent workspace rules, even if a task only references a single file.
- Follow root `AGENTS.md` first.
- For mode behavior, follow `agents/modes.md` and the mode-specific modules referenced there.
- For HornetStudio-specific rules, follow `agents/solution.md`.
- Prefer precise edits in existing files over broad rewrites.
- If multiple rules apply, follow the most specific rule for the affected area.
- If a referenced path or project convention appears stale, search the repository before changing code.
- For larger implementation work, use a new chat or a dedicated handoff when the task would otherwise accumulate too much context.

## Scope Rules

- Keep changes minimal and directly related to the user request.
- Do not introduce broad refactorings, new abstractions, or structural changes unless explicitly requested or required by the active mode rules.
- Prefer existing project patterns, libraries, and local conventions.
- Do not duplicate solution-specific rules here. Maintain them in `agents/solution.md`.

## Validation Rules

- Use the validation and build guidance from `agents/testing.md` and `agents/solution.md`.
- Do not use `dotnet run` as a build check unless explicitly requested.
- For debugging, follow `agents/debugging.md`, including reproducible validation and stop conditions for repeated attempts without measurable progress.
