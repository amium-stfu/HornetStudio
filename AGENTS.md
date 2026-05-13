# AGENTS.md instructions

This root file is the authoritative entry point for repository agent behavior.

## Priority Rules

- If a `MODE` is specified, it has absolute priority for the current request.
- Mode commands are recognized only when they appear as the first non-empty token of the user message.
- Bracketed mode commands are the most stable syntax and should be preferred.
- Short `#` mode commands are supported only as exact command tokens.
- Plain aliases without `#` are not supported.
- If a `MODE` is unclear, ask for clarification before continuing.
- Always answer in German in chat.
- Always write code, comments, XML documentation, UI text, file names, and technical identifiers in English.
- Always write user-visible error and validation messages in English.

## Loading Order

Apply these files in the following order:

1. This root `AGENTS.md`
2. Any mode-specific module referenced by the active mode
3. Supporting thematic modules that apply to the task
4. Always apply [agents/solution.md](agents/solution.md) last for repository-specific rules

If two rules appear to overlap, prefer the more specific rule. If two equally specific rules conflict, this root `AGENTS.md` wins.

All files under `agents/` except [agents/solution.md](agents/solution.md) must remain solution-neutral and reusable as a template for other repositories. Project-specific paths, commands, conventions, workarounds, deprecated names, and domain rules belong only in [agents/solution.md](agents/solution.md).

## Modules

- [agents/planning.md](agents/planning.md) -> PLAN, TODO, workitems, and handoffs
- [agents/implementation.md](agents/implementation.md) -> implementation, build workflow, and change-scope rules
- [agents/fix.md](agents/fix.md) -> lightweight defect fixes without workitem/debug-report overhead
- [agents/clean.md](agents/clean.md) -> CLEAN workflow and cleanup constraints
- [agents/debugging.md](agents/debugging.md) -> debug workflow, report layout, validation, and stop conditions
- [agents/architecture.md](agents/architecture.md) -> architecture, coupling, fragmentation, and repository structure rules
- [agents/naming.md](agents/naming.md) -> language, naming, and path conventions
- [agents/documentation.md](agents/documentation.md) -> XML and Markdown documentation maintenance
- [agents/testing.md](agents/testing.md) -> testing, validation preference order, and build-quality rules
- [agents/release.md](agents/release.md) -> versioning, publish, tags, notice, and release constraints
- [agents/solution.md](agents/solution.md) -> repository-specific conventions and verified solution rules

## Mode Routing

Mode commands are recognized only when they appear as the first non-empty token of the user message.

Preferred command syntax:

- `[MODE: ASK]`
- `[MODE: STRUCTURE]`
- `[MODE: PLAN]`
- `[MODE: TODO]`
- `[MODE: IMPLEMENT]`
- `[MODE: FIX]`
- `[MODE: DEBUG]`
- `[MODE: CLEAN]`
- `[MODE: BUILD]`
- `[MODE: PUBLISH]`

Supported short syntax:

- `#ask` -> `[MODE: ASK]`
- `#struct` -> `[MODE: STRUCTURE]`
- `#plan` -> `[MODE: PLAN]`
- `#todo` -> `[MODE: TODO]`
- `#impl` -> `[MODE: IMPLEMENT]`
- `#fix` -> `[MODE: FIX]`
- `#debug` -> `[MODE: DEBUG]`
- `#clean` -> `[MODE: CLEAN]`
- `#build` -> `[MODE: BUILD]`
- `#publish` -> `[MODE: PUBLISH]`

Mode routing is case-insensitive.

A short `#` mode command is valid only when the first non-empty token is exactly one of the supported commands. The command may be followed by whitespace, a line break, or `:`, but not by additional letters or digits. For example, `#plan` and `#plan:` select `[MODE: PLAN]`; `#planning` does not.

## Mode Modules

- `ASK` -> no workflow module; answer only the question.
- `STRUCTURE` -> [agents/architecture.md](agents/architecture.md)
- `PLAN` -> [agents/planning.md](agents/planning.md)
- `TODO` -> [agents/planning.md](agents/planning.md)
- `IMPLEMENT` -> [agents/implementation.md](agents/implementation.md), [agents/naming.md](agents/naming.md), [agents/documentation.md](agents/documentation.md), [agents/testing.md](agents/testing.md)
- `FIX` -> [agents/fix.md](agents/fix.md), [agents/naming.md](agents/naming.md), [agents/documentation.md](agents/documentation.md), [agents/testing.md](agents/testing.md)
- `DEBUG` -> [agents/debugging.md](agents/debugging.md), [agents/testing.md](agents/testing.md)
- `CLEAN` -> [agents/clean.md](agents/clean.md), [agents/testing.md](agents/testing.md)
- `BUILD` -> [agents/implementation.md](agents/implementation.md) Build section only, [agents/testing.md](agents/testing.md)
- `PUBLISH` -> [agents/release.md](agents/release.md)
