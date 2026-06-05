# Implementation Workflow

## Implementation

- Implement only the requested functionality.
- Only modify files that are necessary for the requested change.
- Do not add extra features, refactorings, or structural changes without asking.
- Keep changes minimal, targeted, and aligned with the existing architecture and naming used in the repository.
- Prefer existing libraries, frameworks, patterns, and project conventions.
- Prefer extending existing components over introducing new helper layers, wrapper classes, or intermediary abstractions unless technically required.
- Preserve existing project conventions, file structure, and framework patterns before proposing alternative patterns.
- Change only code and files that are directly related to the request.
- Do not make changes outside the requested scope without asking.

## Workflow

- At the start of `IMPLEMENT`, check whether `docs/workitems/active.yaml` exists.
- Resolve `docs/workitems/active.yaml` relative to the repository root that contains root `AGENTS.md`, not relative to the current editor file, project file, or solution folder.
- If `docs/workitems/active.yaml` exists, read it before making implementation decisions.
- Treat `docs/workitems/active.yaml` as a FIFO queue where the first `queue` entry is the active implementation workitem.
- When the first `queue` entry contains valid repository-relative workitem and implementation handoff paths, use the referenced handoff as the primary execution source.
- Do not remove queue entries during `IMPLEMENT`; completion is handled by `DONE`.
- If the user provides explicit implementation instructions in the current request, those instructions take priority over the active handoff unless they conflict with it.
- If the current request conflicts with the active handoff, stop and ask for clarification before changing files.
- If `docs/workitems/active.yaml` is missing, malformed, empty, or references missing paths, do not infer the intended workitem; ask for clarification before using a handoff-driven workflow.
- For larger changes, create a short plan first and wait for confirmation unless the user explicitly requests implementation.
- Do not start implementation without explicit request or confirmation.
- Keep refactorings small and understandable.
- Do not silently restructure existing working code.
- Do not make silent architecture decisions.
- Do not introduce new classes, services, or abstractions without clear benefit.
- If a chat becomes too complex, offer a concise implementation handoff for a new chat.
- After changes, briefly check build, tests, formatting, and warnings.
- At the end of every larger change, provide a short summary with changed files.
- If a rule requires related documentation to be updated, perform that documentation update in the same change.

## Build

- Build the complete solution without starting any application.
- Prefer the repository-specific build command defined in the solution rules when restore has already completed.
- Do not use `dotnet run`.
- Do not start demo, UI, service, or worker projects.
- Report build success, warnings, and errors concisely.
