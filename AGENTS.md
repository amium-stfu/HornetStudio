# AGENTS.md instructions for b:\HornetStudio

## Priority Rules

- If a `MODE` is specified, it has absolute priority for the current request.
- Mode aliases are treated exactly like explicit `MODE` instructions.
- If a `MODE` is unclear, ask for clarification before continuing.
- Always answer in German in chat.
- Always write code, comments, XML documentation, UI text, file names, and technical identifiers in English.
- Always write user-visible error and validation messages in English.
- Prefer existing libraries, frameworks, patterns, and project conventions.
- Change only code and files that are directly related to the request.
- Do not make changes outside the requested scope without asking.

## Mode Aliases

- `ask` -> `[MODE: ASK]`
- `struct` -> `[MODE: STRUCTURE]`
- `plan` -> `[MODE: PLAN]`
- `todo` -> `[MODE: TODO]`
- `impl` -> `[MODE: IMPLEMENT]`
- `debug` -> `[MODE: DEBUG]`
- `clean` -> `[MODE: CLEAN]`
- `publish` -> `[MODE: PUBLISH]`

If a message starts with one of these aliases, interpret it as the corresponding `MODE`.

## MODE Rules

### ASK

- Answer only the question.
- Do not provide code.
- Do not implement changes.

### STRUCTURE

- Work out architecture, structure, responsibilities, and boundaries.
- Do not provide complete code.
- Do not implement changes.
- Do not create workitem folders automatically.

### PLAN

- Create a clear, actionable step-by-step plan.
- Do not provide code.
- Do not implement changes.
- Creating a plan is the trigger for a workitem folder.
- Create or reuse a matching folder under `docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/`.
- Store plan files in `plans/`.
- After completing the plan, always create a dedicated implementation handoff in `handoffs/`.
- Never overwrite an existing plan or handoff file. Create a new timestamped file instead.
- Keep plan and handoff files concise, self-contained, and optimized for a new chat with minimal context.
- Write the plan for alignment and decision-making.
- Write the implementation handoff as an execution-ready package for another model or a new chat.
- Assume the implementation model has less context and should not have to infer missing scope, task order, or target files.

#### Required PLAN Files

- `docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/plans/<yyyy.MM.dd.HHmm>-plan.md`
- `docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/handoffs/<yyyy.MM.dd.HHmm>-implementation-handoff.md`

#### PLAN Requirements

- Focus on problem framing, scope, architecture, sequencing, and major decisions.
- Keep the plan readable for humans and suitable for review.
- Do not rely on the plan alone for implementation handoff quality.
- Move execution detail into the implementation handoff.

#### IMPLEMENTATION HANDOFF Requirements

- Make the handoff specific enough that another model can execute it with minimal interpretation.
- Prefer concrete file responsibilities over abstract work packages.
- Break larger work into ordered steps that can be completed sequentially.
- State missing information, open questions, or decision dependencies explicitly.
- Define how success will be verified.

#### Required IMPLEMENTATION HANDOFF Structure

```md
# IMPLEMENTATION HANDOFF

## Goal
Clear description of the objective.

## Scope
What is included and what is explicitly NOT included.

## Starting Point
- Current behavior
- Relevant assumptions
- Required prerequisites

## Tasks
1. Task description
2. Task description
3. Task description

## File-Level Changes
- Path/FileName -> exact responsibility
- Path/FileName -> exact responsibility

## Implementation Order
1. First concrete change
2. Second concrete change
3. Final integration step

## Technical Constraints
- Frameworks, libraries, patterns that must be used
- Relevant project rules

## Acceptance Criteria
- Observable result 1
- Observable result 2
- Observable result 3

## Verification
- Build/test command or manual verification step
- Build/test command or manual verification step

## Out of Scope
- Explicitly excluded work

## Risks / Watchouts
- Important edge case or failure mode

## Relevant Files (if known)
- Path/FileName
- Path/FileName

## Notes
- Important edge cases or constraints
```

### TODO

- Use `TODO` for non-urgent bugs, technical debt, follow-up ideas, or postponed work.
- Do not implement changes when creating a todo entry.
- Create `docs/todos/` if it does not exist.
- Store each todo as a separate Markdown file in `docs/todos/`.
- Use the file name format `<yyyy.MM.dd.HHmm>-<slug>.md`.
- Keep todo entries short, specific, and actionable.
- If a todo is later selected for active work, create a `PLAN` and move forward with a workitem.

#### Required TODO File Structure

```md
# TODO

## Title
Short task title.

## Problem
Short description of the issue or follow-up.

## Impact
Why this matters.

## Suggested Fix
Optional implementation direction.

## Priority
Low / Medium / High

## Related Files
- Path/FileName

## Notes
- Optional context
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
- If a matching workitem exists, create or update debug files inside its `debug/` folder.
- Do not use a global `Debug.md` for new debugging work.

#### Required DEBUG Structure

- Store debug reports in `docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/debug/`.
- Use timestamped file names: `<yyyy.MM.dd.HHmm>-debug.md`.
- Keep only relevant, current debugging history.
- Remove outdated or disproven attempts when updating a debug report.

#### Required DEBUG REPORT Structure

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
- Remove unused code, obsolete comments, temporary debug output, and dead branches.
- Improve exception handling where it is incomplete, unclear, or too broad.
- Replace silent `catch` blocks with explicit handling, logging, or rethrowing with context.
- Avoid UI blocking by replacing blocking calls such as `.Result`, `.Wait()`, `Thread.Sleep`, or long-running work on the UI thread.
- Improve readability with small, local changes only.
- Preserve public APIs unless changing them is necessary and explicitly approved.
- Update tests only when cleanup affects tested behavior or exposes missing coverage.
- If cleanup reveals a behavioral bug, switch to `[MODE: DEBUG]` or ask before fixing it.
- After cleanup, run relevant build or tests when feasible.

### PUBLISH

- Create a release publish only.
- Use the publish rules defined under `Versioning and Releases`.
- Do not add features, refactorings, or unrelated changes.
- Update release documentation only when required by the release rules.

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

## Workitem Rules

- A workitem folder represents one concrete planned topic.
- `PLAN` is the trigger that creates the workitem folder.
- `STRUCTURE` alone does not require a workitem folder.
- Reuse an existing workitem folder when the topic is clearly the same.
- Create a new workitem folder when the topic is new or the scope has materially changed.
- Use short lowercase slugs with hyphens.

### Recommended Workitem Layout

```text
docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/
  plans/
  debug/
  handoffs/
```

## Coding Style

- Prefer named parameters for method calls instead of purely positional arguments, especially when there are more than two parameters, parameters have similar types, or the meaning is unclear.
- Keep code readable, maintainable, and clearly structured for humans.
- Prefer descriptive names, small methods, and focused classes.
- Avoid magic numbers and duplication.
- Follow the existing architecture, patterns, and naming conventions.
- Avoid overengineering and unnecessary abstractions.

## Documentation

- Always document public methods with XML summaries and parameters.
- Always create or maintain XML documentation for changed public classes.
- Update relevant Markdown documentation when setup, behavior, structure, or usage changes.
- Maintain `AGENTS.md` with project-specific rules.
- Maintain `CHANGELOG.md` for relevant changes.
- Maintain `TODO.md` or `ROADMAP.md` only when already present or clearly useful.
- Use `docs/todos/` for standalone backlog entries created through `TODO` mode.

## Structure

- Create a clean folder structure when topics can be clearly separated.
- When creating a solution, use a `./src` directory structure.
- Create a dedicated directory for each project inside `./src`.
- Group related classes, derived types, functions, and resources meaningfully.
- Avoid unnecessary fragmentation when fewer files make the structure easier to read.

## Tests and Quality

- Create or update appropriate unit tests for new or changed logic.
- Handle errors explicitly and clearly.
- Do not use silent `catch` blocks.
- Use meaningful logging for errors and important workflows.
- Exceptions should be descriptive and include relevant context.
- Use `async` and `await` correctly and avoid blocking calls such as `.Result` or `.Wait()`.
- Use nullable reference types deliberately and avoid unnecessary null risks.
- Follow existing `.editorconfig` and formatting rules.

## Git and Security

- Create an appropriate `.gitignore` if none exists.
- Do not commit secrets, tokens, passwords, or machine-specific local paths.
- Change public APIs only when necessary, and point out breaking changes.
- Do not introduce new dependencies without justification.
- When adding runtime dependencies, briefly check license and FOSS relevance.

## Versioning and Releases

- Every release version must have a unique version number.
- The version number is based on a timestamp in the format `yyyy.MM.dd.HHmm`, for example `2026.04.27.1430`.
- Generate version numbers only for releases, not for local builds.
- Every release version must be unique and monotonically increasing.
- There must be exactly one version per release as the single source of truth.
- Use version numbers consistently in code, packages, Git tags, and documentation.
- For every release, update `CHANGELOG.md` and relevant documentation.

### Publish

- Publish output must be written to `./Release`.
- Publish builds must be self-contained.
- Publish builds must use single-file output.
- For .NET projects, use `SelfContained=true` and `PublishSingleFile=true`.

### Assembly / NuGet

- Use the version number without a prefix, for example `2026.04.27.1430`.
- Use this version in project files, assembly metadata, and NuGet packages.

### Git

- Use a leading `v` for Git tags, for example `v2026.04.27.1430`.

## FOSS and License Documentation

- Maintain a notice file with runtime FOSS components for releases.
- Do not include test or development dependencies in the notice file unless they are required at runtime.
- The notice file should include name, version, copyright information, license type, and full license texts.
