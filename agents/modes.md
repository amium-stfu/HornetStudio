# Modes

## Mode Aliases

Preferred command syntax:

- `#ask` -> `[MODE: ASK]`
- `#struct` -> `[MODE: STRUCTURE]`
- `#plan` -> `[MODE: PLAN]`
- `#todo` -> `[MODE: TODO]`
- `#impl` -> `[MODE: IMPLEMENT]`
- `#debug` -> `[MODE: DEBUG]`
- `#clean` -> `[MODE: CLEAN]`
- `#build` -> `[MODE: BUILD]`
- `#publish` -> `[MODE: PUBLISH]`

If a message starts with one of these `#` commands, interpret it as the corresponding `MODE`.

Legacy aliases remain supported only when the message starts exactly with the alias as the first token:

- `ask` -> `[MODE: ASK]`
- `struct` -> `[MODE: STRUCTURE]`
- `plan` -> `[MODE: PLAN]`
- `todo` -> `[MODE: TODO]`
- `impl` -> `[MODE: IMPLEMENT]`
- `debug` -> `[MODE: DEBUG]`
- `clean` -> `[MODE: CLEAN]`
- `build` -> `[MODE: BUILD]`
- `publish` -> `[MODE: PUBLISH]`

## ASK

- Answer only the question.
- Do not provide code.
- Do not implement changes.

## STRUCTURE

- Work out architecture, structure, responsibilities, and boundaries.
- Critically evaluate the proposed idea before accepting it.
- Explicitly assess whether the idea is useful, necessary, maintainable, performant, and aligned with the existing project architecture.
- Point out risks, problematic assumptions, unnecessary complexity, performance costs, coupling, migration effort, and possible regressions.
- Contradict the proposal clearly and constructively when it is technically weak, overengineered, risky, or not aligned with the project.
- Prefer simpler alternatives when they solve the same problem with less complexity or lower risk.
- Do not provide complete code.
- Do not implement changes.
- Do not create workitem folders automatically.
- Apply the cross-cutting architecture rules from [architecture.md](architecture.md).

## PLAN

- Create a concise implementation handoff instead of a separate plan document.
- Do not provide code.
- Do not implement changes.
- Apply the planning, workitem, and handoff rules from [planning.md](planning.md).

## TODO

- Use `TODO` for non-urgent bugs, technical debt, follow-up ideas, or postponed work.
- Do not implement changes while creating a todo entry.
- Apply the todo file rules from [planning.md](planning.md).

## IMPLEMENT

- Implement only the requested functionality.
- Only modify files that are necessary for the requested change.
- Do not add extra features, refactorings, or structural changes without asking.
- Keep chat comments to an absolute minimum during implementation.
- Only comment in chat when user input, a decision, or intervention is required.
- Provide the final implementation summary only after the work is complete.
- Apply the workflow rules from [implementation.md](implementation.md), the naming rules from [naming.md](naming.md), the documentation rules from [documentation.md](documentation.md), the validation rules from [testing.md](testing.md), and the solution-specific rules from [solution.md](solution.md).

## DEBUG

- Before debugging, verify that the matching workitem is known.
- Do not start debugging without a known workitem.
- First analyze and explain the root cause.
- Do not jump directly to code without explaining the issue.
- Then propose a solution.
- Provide code only when useful or explicitly requested.
- Apply the detailed debug workflow from [debugging.md](debugging.md) and the validation preference order from [testing.md](testing.md).

## CLEAN

- Clean up existing code without changing intended behavior.
- Do not add new features.
- Apply the cleanup rules from [clean.md](clean.md) and the validation rules from [testing.md](testing.md).

## BUILD

- Build the complete solution without starting any application.
- Do not implement code changes while in `BUILD` mode unless the user explicitly switches to an implementation or debug mode.
- Apply the build rules from [implementation.md](implementation.md), the validation rules from [testing.md](testing.md), and the solution-specific command preferences from [solution.md](solution.md).

## PUBLISH

- Create a release publish only.
- Use the publish rules from [release.md](release.md).
- Do not add features, refactorings, or unrelated changes.
- Update release documentation only when required by the release rules.
