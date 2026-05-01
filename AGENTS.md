# AGENTS.md instructions for b:\HornetStudio

## Mode Control

- If a `MODE` is specified, it has absolute priority.
- MODE instructions override all other rules for the current request.
- Mode aliases are treated as full MODE instructions.
- Never deviate from the active `MODE`.
- If a `MODE` is unclear, ask for clarification.

## Mode Aliases (Short Commands)

The following short commands are treated as MODE instructions:

- ask → [MODE: ASK]
- struct → [MODE: STRUCTURE]
- plan → [MODE: PLAN]
- impl → [MODE: IMPLEMENT]
- debug → [MODE: DEBUG]
- clean -> [MODE: CLEAN]
- publish -> [MODE: PUBLISH]

Rules:
- If a message starts with one of these aliases, it must be interpreted as the corresponding MODE.
- The alias has the same priority as an explicit [MODE: ...] instruction.
- No additional MODE parsing is required if an alias is used.

## MODE Rules

### ASK

- Answer only the question.
- Do not provide code.
- Do not implement changes.

### STRUCTURE

- Work out architecture, structure, and separation of responsibilities.
- Do not provide complete code.

### PLAN

- Create a clear, actionable step-by-step plan.
- Do not provide code.

#### Planning Handoff (Post-PLAN Requirement)

- After completing a PLAN, always generate a dedicated Markdown handoff for implementation in a new chat.
- Store every implementation handoff as a separate Markdown file in `docs/handoffs/`.
- Create `docs/handoffs/` if it does not exist.
- Use the file name format `yyyy.MM.dd.HHmm-implementation-handoff.md`.
- Never overwrite an existing handoff file; create a new timestamped file instead.
- This handoff must be clearly separated and self-contained.
- The handoff must NOT include discussion, reasoning history, or alternatives.
- The handoff must be optimized for minimal context usage in a new chat.

##### Handoff Requirements

- The handoff must be concise but complete enough for direct implementation.
- Only include information that is required for execution.
- Avoid redundant explanations.

##### Required Structure

```md
# IMPLEMENTATION HANDOFF

## Goal
Clear description of the objective.

## Scope
What is included and what is explicitly NOT included.

## Tasks
1. Task description
2. Task description
3. Task description

## Technical Constraints
- Frameworks, libraries, patterns that must be used
- Relevant project rules

## Relevant Files (if known)
- Path/FileName
- Path/FileName

## Notes
- Important edge cases or constraints
```

### IMPLEMENT

- Implement only the requested functionality.
- Only modify files that are necessary for the requested change.
- Do not add extra features, refactorings, or structural changes without asking.
- Keep chat comments to an absolute minimum during implementation.
- Only comment in chat when user input, a decision, or intervention is required.
- Provide the final implementation summary only after the work is complete.

### DEBUG

- First analyze and explain the root cause.
- Do not jump directly to code without explaining the issue.
- Then propose a solution.
- Provide code only when useful or explicitly requested.

#### Debug Documentation (DEBUG Mode Requirement)

- During debugging, always create or update a `Debug.md`.
- The goal is to prevent repeated failed solutions and reduce context size.

##### Debug.md Requirements

- Must be concise, structured, and continuously updated.
- Remove outdated or irrelevant attempts.
- Focus on the current state of the problem.

##### Required Structure

```md
# DEBUG REPORT

## Problem
Short and precise description.

## Expected Behavior
What should happen.

## Actual Behavior
What actually happens.

## Error Messages / Logs
Relevant logs or errors.

## Relevant Code
Only the necessary parts.

## Attempted Fixes
- Fix 1 -> Result
- Fix 2 -> Result
- Fix 3 -> Result

## Current Hypothesis (optional)
- Possible root causes
```

### CLEAN

- Clean up existing code without changing intended behavior.
- Do not add new features.
- Do not perform large architectural refactorings without asking.
- Remove unused code, obsolete comments, temporary debug output, and dead branches.
- Improve exception handling where it is incomplete, unclear, or too broad.
- Replace silent `catch` blocks with explicit handling, logging, or rethrowing with context.
- Avoid UI blocking by replacing blocking calls such as `.Result`, `.Wait()`, `Thread.Sleep`, or long-running work on the UI thread.
- Improve readability with small, local changes only.
- Preserve public APIs unless changing them is necessary and explicitly approved.
- Update tests only when cleanup affects tested behavior or exposes missing coverage.
- If cleanup reveals a behavioral bug, switch to [MODE: DEBUG] or ask before fixing it.
- After cleanup, run relevant build/tests when feasible.

### PUBLISH

- Create a release publish only.
- Use the publish rules defined under `Versioning and Releases`.
- Do not add features, refactorings, or unrelated changes.
- Update release documentation only when required by the release rules.

## General

- Always answer in German in chat.
- Always write code, comments, XML documentation, UI text, file names, and technical identifiers in English.
- Always write user-visible error and validation messages in English.
- Prefer libraries, frameworks, and patterns already present in the project.
- Do not introduce new dependencies without justification.
- Avoid overengineering and unnecessary abstractions.
- Follow existing `.editorconfig` and formatting rules.
- Change only code that is directly related to the task.

## Coding Style

- Prefer named parameters for method calls instead of purely positional arguments, especially when:
  - there are more than two parameters,
  - parameters have similar types,
  - the meaning is unclear.
- Code should be readable, maintainable, and clearly structured for humans.
- Prefer descriptive names, small methods, and focused classes.
- Avoid magic numbers and duplication.
- Follow the existing architecture, patterns, and naming conventions of the project.

## Documentation

- Always document public methods with XML summaries and parameters.
- Always create and maintain XML documentation for classes when they are changed.
- Maintain or update structural plans in Markdown when architecture, folder structure, or larger workflows change.
- Update `README.md` or other relevant Markdown documentation when setup, behavior, or usage changes.

## Structure

- Create a clean folder structure when topics can be clearly separated.
- When creating a solution, use a `./src` directory structure.
- Create a dedicated directory for each project inside `./src`.
- Group related classes, derived types, functions, and resources meaningfully.
- Avoid unnecessary fragmentation when a few files make the structure easier to read.

## Tests and Quality

- Create or update appropriate unit tests for new or changed logic.
- Handle errors explicitly and clearly.
- Do not use silent `catch` blocks.
- Use meaningful logging for errors and important workflows.
- Exceptions should be descriptive and include relevant context.
- Use `async` and `await` correctly and avoid blocking calls such as `.Result` or `.Wait()`.
- Use nullable reference types deliberately and avoid unnecessary null risks.

## Git and Security

- Create an appropriate `.gitignore` if none exists.
- Do not commit secrets, tokens, passwords, or machine-specific local paths.
- Change public APIs only when necessary, and point out breaking changes.

## Workflow

- For larger changes, create a short plan first and wait for confirmation.
- Do not start implementation without explicit request or confirmation.
- Keep refactorings small and understandable.
- Do not unnecessarily restructure existing working code.
- Do not make silent architecture decisions.
- Do not introduce new classes, services, or abstractions without clear benefit.
- Maintain an `AGENTS.md` with project-specific rules.
- Maintain `CHANGELOG.md` for relevant changes.
- Maintain `TODO.md` or `ROADMAP.md` only when already present or clearly useful.
- If a chat becomes too complex, offer to create a concise chat handoff for continuing in a new chat.
- After changes, briefly check build, tests, formatting, and warnings.
- Do not make changes outside the requested scope without asking.
- At the end of every larger change, provide a short summary with changed files.

## Versioning and Releases

- Every release version must have a unique version number.
- The version number is based on a timestamp in the format `yyyy.MM.dd.HHmm`, for example `2026.04.27.1430`.

### Publish

- Publish output must be written to `./Release`.
- Publish builds must be self-contained.
- Publish builds must use single-file output.
- For .NET projects, use `SelfContained=true` and `PublishSingleFile=true`.

### Assembly / NuGet

- Use the version number without a prefix, for example `2026.04.27.1430`.
- This version is used in project files, assembly metadata, and NuGet packages.

### Git

- Use a leading `v` for Git tags, for example `v2026.04.27.1430`.

### Rules

- Generate version numbers only for releases, not for local builds.
- Every release version must be unique and monotonically increasing.
- There must be exactly one version per release as the single source of truth.
- Version numbers must be used consistently in code, packages, Git tags, and documentation.
- For every release, update `CHANGELOG.md` and relevant documentation.

## FOSS and License Documentation

- When adding new runtime dependencies, check whether FOSS-relevant components were added.
- Do not include test and development dependencies in the notice file unless they are required at runtime.
- Maintain a notice file with runtime FOSS components for releases.
- The notice file should include name, version, copyright information, license type, and full license texts.
- Do not add a new external dependency without briefly checking license and FOSS relevance.
