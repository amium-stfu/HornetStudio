# Workspace Instructions

These instructions are repository-wide rules for GitHub Copilot in Visual Studio Code and Visual Studio. Apply them for all code generation, edits, refactorings, documentation updates, and file creation in this workspace.

The primary workspace-wide agent behavior, workflow, mode rules, and project-specific rules are defined in the repository root `AGENTS.md` and the modules under `agents/`.

Use this file only as Copilot-specific routing and execution context. Do not treat it as a separate source of truth.

## Copilot Behavior

- Interpret these instructions as persistent workspace rules, even if a task only references a single file.
- Follow root `AGENTS.md` first.
- For mode behavior, use the mode routing table in root `AGENTS.md` and then read the mode-specific modules referenced there.
- For HornetStudio-specific rules, follow `agents/solution.md`.
- Prefer precise edits in existing files over broad rewrites.
- If multiple rules apply, follow the most specific rule for the affected area.
- If a referenced path or project convention appears stale, search the repository before changing code.
- For larger implementation work, use a new chat or a dedicated handoff when the task would otherwise accumulate too much context.

## Mode Routing

- Recognize mode commands only when they are the first non-empty token of the user message, following the rules in root `AGENTS.md`.
- Supported short mode commands include `#ask`, `#struct`, `#plan`, `#todo`, `#done`, `#impl`, `#fix`, `#debug`, `#clean`, `#build`, and `#publish`.
- When a mode is recognized, read root `AGENTS.md`, the referenced mode module, any task-relevant supporting modules, and `agents/solution.md` before answering or changing files.
- Do not answer from this Copilot instructions file alone when a mode has a referenced module.

## Active Workitem Handoff

- Treat `docs/workitems/active.yaml` as the repository-relative FIFO queue from planning to implementation.
- The path is relative to the workspace root that contains root `AGENTS.md`; do not resolve it relative to the current editor file, project file, or solution folder.
- For `#impl` / `IMPLEMENT`, read `docs/workitems/active.yaml` before making implementation decisions.
- The first `queue` entry is the active implementation workitem.
- If the first `queue` entry contains a workitem path and an implementation handoff path, read the referenced handoff and use it as the primary execution source.
- If `docs/workitems/active.yaml` is missing, malformed, empty, or points to missing files, stop and ask for clarification instead of searching for a different active handoff.
- For `#done` / `DONE`, remove only the first `queue` entry from `docs/workitems/active.yaml`; do not implement code, edit workitem history, or remove any other queue entry.
- If the current user request gives explicit implementation instructions, those instructions take priority over the active handoff unless they conflict with it.
- If the current request conflicts with the active handoff, stop and ask which source should be followed.

## Scope Rules

- Keep changes minimal and directly related to the user request.
- Do not introduce broad refactorings, new abstractions, or structural changes unless explicitly requested or required by the active mode rules.
- Prefer existing project patterns, libraries, and local conventions.
- Do not duplicate solution-specific rules here. Maintain them in `agents/solution.md`.

## Validation Rules

- Use the validation and build guidance from `agents/testing.md` and `agents/solution.md`.
- Do not use `dotnet run` as a build check unless explicitly requested.
- For debugging, follow `agents/debugging.md`, including reproducible validation and stop conditions for repeated attempts without measurable progress.
