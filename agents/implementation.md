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
