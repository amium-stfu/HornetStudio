# Cleanup Workflow

## CLEAN

- Clean up existing code without changing intended behavior.
- Do not add new features.
- Remove unused code, obsolete comments, temporary debug output, and dead branches.
- Simplify working code when the result is easier for a human maintainer to understand in one pass.
- Keep cleanup minimal, local, and domain-focused.
- Improve exception handling where it is incomplete, unclear, or too broad.
- Replace silent `catch` blocks with explicit handling, logging, or rethrowing with context.
- Avoid UI blocking by replacing blocking calls such as `.Result`, `.Wait()`, `Thread.Sleep`, or long-running work on the UI thread.
- Improve readability with small, local changes only.
- Preserve public APIs unless changing them is necessary and explicitly approved.
- Update tests only when cleanup affects tested behavior or exposes missing coverage.
- If cleanup reveals a behavioral bug, switch to `[MODE: DEBUG]` or ask before fixing it.
- Stop and ask before continuing if simplification would require behavioral changes, architecture changes, or unclear tradeoffs.
- After cleanup, run relevant build or tests when feasible.

## Shared Code Style

`CLEAN` also applies [agents/human-first.md](human-first.md) for shared readability-first code style.
