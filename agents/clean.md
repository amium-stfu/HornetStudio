# Cleanup Workflow

## CLEAN

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
