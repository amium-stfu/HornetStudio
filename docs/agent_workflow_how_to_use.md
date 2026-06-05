# Agent Workflow How To Use

## Purpose

This workflow separates planning, implementation, and completion tracking.

`docs/workitems/active.yaml` is the only active workitem queue. It is intentionally small and contains no history. Workitem history stays in `docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/`.

## Queue File

Path:

```text
docs/workitems/active.yaml
```

Structure:

```yaml
queue:
  - workitem: docs/workitems/2026.06.01.1243-agent_active_yaml_queue/
    handoff: docs/workitems/2026.06.01.1243-agent_active_yaml_queue/handoffs/2026.06.01.1243-implementation-handoff.md
```

Empty queue:

```yaml
queue: []
```

Rules:

- The first `queue` entry is the active workitem.
- `#plan` appends a new entry to the end of `queue`.
- `#impl` implements only the first entry.
- `#done` removes only the first entry.
- Completed entries are not kept in `active.yaml`.
- Do not add summaries, status fields, priorities, timestamps, or history to `active.yaml`.

## Standard Workflow

1. Discuss structure or direction:

```text
#struct <topic>
```

Use this before planning when the architecture, boundaries, or task split are still unclear.

2. Create an implementation handoff:

```text
#plan <task>
```

Result:

- Creates or reuses `docs/workitems/<timestamp>-<slug>/`.
- Creates a handoff under `handoffs/`.
- Appends the workitem and handoff path to `docs/workitems/active.yaml`.

3. Implement the active queue entry:

```text
#impl
```

Result:

- Reads the first `queue` entry.
- Loads the referenced handoff.
- Implements only that handoff.
- Leaves the queue unchanged.

4. Complete the active queue entry:

```text
#done
```

Result:

- Removes only the first `queue` entry.
- Does not edit historical workitem files.
- Does not implement code.

5. Continue with the next entry:

```text
#impl
```

The next queue entry becomes active automatically because it is now first.

## Parallel Planning Workflow

Use this when planning should stay ahead of implementation:

```text
#plan Task A
#plan Task B
#plan Task C
```

Queue result:

```yaml
queue:
  - workitem: docs/workitems/<timestamp>-task_a/
    handoff: docs/workitems/<timestamp>-task_a/handoffs/<timestamp>-implementation-handoff.md
  - workitem: docs/workitems/<timestamp>-task_b/
    handoff: docs/workitems/<timestamp>-task_b/handoffs/<timestamp>-implementation-handoff.md
  - workitem: docs/workitems/<timestamp>-task_c/
    handoff: docs/workitems/<timestamp>-task_c/handoffs/<timestamp>-implementation-handoff.md
```

Then process one item at a time:

```text
#impl
#done
#impl
#done
#impl
#done
```

## When To Use Other Modes

- `#ask`: Answer only a question. No implementation.
- `#struct`: Evaluate architecture, structure, and task split. No full code.
- `#plan`: Create a workitem and implementation handoff.
- `#todo`: Capture a postponed issue or follow-up idea.
- `#impl`: Implement the first queue entry.
- `#done`: Remove the completed first queue entry.
- `#fix`: Apply a focused defect fix.
- `#debug`: Analyze root cause first, then propose or apply a fix when appropriate.
- `#clean`: Perform scoped cleanup.
- `#build`: Build verification only.
- `#publish`: Release workflow.
- `#bench`: Benchmark workflow.

## Recommended Habits

- Keep each workitem focused on one concrete implementation topic.
- Use `#struct` before `#plan` when the shape of the work is uncertain.
- Use several small `#plan` calls instead of one oversized handoff.
- Run `#done` only after the active workitem is actually complete.
- Do not manually reorder the queue unless the implementation order intentionally changes.
- Do not use `active.yaml` as a changelog or status board.

## Error Cases

If `#impl` is used and `queue` is empty:

- Stop.
- Report that no active workitem exists.
- Do not guess a handoff.

If `#done` is used and `queue` is empty:

- Stop.
- Report that no active workitem exists.
- Do not edit the file.

If the first queue entry references missing files:

- Stop.
- Report the invalid path.
- Ask for clarification instead of selecting another entry.

