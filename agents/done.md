# Done Workflow

## DONE

- Use `DONE` only to complete the first active workitem queue entry.
- Do not implement code, create workitems, or edit workitem history in `DONE`.
- Read `docs/workitems/active.yaml` relative to the repository root that contains root `AGENTS.md`.
- If `docs/workitems/active.yaml` is missing, stop and report that no active workitem exists.
- If `docs/workitems/active.yaml` is malformed, has no top-level `queue`, or `queue` is not a list, stop and report that the active workitem queue is invalid.
- If `queue` is empty, stop and report that no active workitem exists.
- Remove only the first `queue` entry.
- Do not remove, reorder, rewrite, or mark any other queue entry.
- Keep `docs/workitems/active.yaml` compact and preserve repository-relative paths.
- Keep the chat response short and state which workitem was completed.

## Required Active Queue File

- `docs/workitems/active.yaml`

## Required Active Queue Structure

```yaml
queue:
  - workitem: docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/
    handoff: docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/handoffs/<yyyy.MM.dd.HHmm>-implementation-handoff.md
```

## Empty Active Queue Structure

```yaml
queue: []
```
