# AGENTS.md instructions for b:\HornetStudio

This root file is the authoritative entry point for repository agent behavior.

## Priority Rules

- If a `MODE` is specified, it has absolute priority for the current request.
- Mode aliases are treated exactly like explicit `MODE` instructions.
- If a `MODE` is unclear, ask for clarification before continuing.
- Always answer in German in chat.
- Always write code, comments, XML documentation, UI text, file names, and technical identifiers in English.
- Always write user-visible error and validation messages in English.

## Loading Order

Apply these files in the following order:

1. This root `AGENTS.md`
2. [agents/AGENTS.md](agents/AGENTS.md)
3. [agents/modes.md](agents/modes.md)
4. Any mode-specific module referenced by the active mode
5. Supporting thematic modules that apply to the task
6. [agents/solution.md](agents/solution.md) for HornetStudio-specific rules

If two rules appear to overlap, prefer the more specific rule. If two equally specific rules conflict, this root `AGENTS.md` wins.

## Modules

- [agents/AGENTS.md](agents/AGENTS.md) -> local index for the modular rule set
- [agents/modes.md](agents/modes.md) -> mode aliases and mode-specific behavior
- [agents/planning.md](agents/planning.md) -> PLAN, TODO, workitems, and handoffs
- [agents/implementation.md](agents/implementation.md) -> implementation, build workflow, and change-scope rules
- [agents/clean.md](agents/clean.md) -> CLEAN workflow and cleanup constraints
- [agents/debugging.md](agents/debugging.md) -> debug workflow, report layout, validation, and stop conditions
- [agents/architecture.md](agents/architecture.md) -> architecture, coupling, fragmentation, and repository structure rules
- [agents/naming.md](agents/naming.md) -> language, naming, and path conventions
- [agents/documentation.md](agents/documentation.md) -> XML and Markdown documentation maintenance
- [agents/testing.md](agents/testing.md) -> testing, validation preference order, and build-quality rules
- [agents/release.md](agents/release.md) -> versioning, publish, tags, notice, and release constraints
- [agents/solution.md](agents/solution.md) -> HornetStudio-specific conventions and verified solution rules

## Mode Routing

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

If a message starts with one of these `#` commands, interpret it as the corresponding `MODE` and then apply [agents/modes.md](agents/modes.md) plus the linked supporting modules for that mode.

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
