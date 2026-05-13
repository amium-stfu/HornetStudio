# Planning and Workitems

## PLAN

- Before creating a plan, workitem, or handoff, check whether the goal, scope, constraints, dependencies, and acceptance criteria are sufficiently defined for planning.
- If plan-relevant information is missing, do not create a plan, do not create a workitem folder, and do not create a handoff.
- Instead, list the open questions in a concise, structured way and stop after clarification is requested.
- The agent decides whether a plan is small enough for chat or requires a workitem and handoff.
- If the classification is unclear, ask the user before creating files.
- Use a small chat-only plan when the task is local, low-risk, clearly scoped, and can be completed in one session.
- Create or reuse a matching folder under `docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/` when the task is multi-step, cross-cutting, risky, architecture-relevant, intended for later handoff, or explicitly requested as a handoff.
- Create a dedicated implementation handoff in `handoffs/` only when a workitem-worthy plan requires durable handoff context.
- Never overwrite an existing handoff file. Create a new timestamped file instead.
- Keep the handoff concise, self-contained, and optimized for a new chat with minimal context.
- Do not create a separate `plan.md` file unless the user explicitly asks for one.
- For small chat-only plans, write the plan directly in chat and do not create files.
- For workitem-worthy plans, keep the chat response short and state whether blockers remain and which handoff file was created.
- Write implementation handoffs as execution-ready packages for another model or a new chat.
- Assume the implementation model has less context and should not have to infer missing scope, task order, or target files.

## Required PLAN File

- `docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/handoffs/<yyyy.MM.dd.HHmm>-implementation-handoff.md`

## PLAN Requirements

- First validate that no major planning blockers remain.
- If blockers exist, output only the open questions needed to proceed.
- If the task is small, local, and low-risk, put the plan directly in chat.
- If the task is workitem-worthy, put the planning detail into the implementation handoff.
- For handoff plans, keep the chat response short and avoid duplicating the handoff content.
- Use a separate `plans/` folder or `plan.md` file only when explicitly requested.

## IMPLEMENTATION HANDOFF Requirements

- Make the handoff specific enough that another model can execute it with minimal interpretation.
- Prefer concrete file responsibilities over abstract work packages.
- Break larger work into ordered steps that can be completed sequentially.
- State missing information, open questions, or decision dependencies explicitly.
- Define how success will be verified.

## Required IMPLEMENTATION HANDOFF Structure

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

## TODO

- Use `TODO` for non-urgent bugs, technical debt, follow-up ideas, or postponed work.
- Do not implement changes when creating a todo entry.
- Create `docs/todos/` if it does not exist.
- Store each todo as a separate Markdown file in `docs/todos/`.
- Use the file name format `<yyyy.MM.dd.HHmm>-<slug>.md`.
- Use `snake_case` for todo slugs.
- Keep todo entries short, specific, and actionable.
- If a todo is later selected for active work, create a `PLAN` and move forward with a workitem.

## Required TODO File Structure

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

## Workitem Rules

- A workitem folder represents one concrete planned topic.
- `PLAN` creates a workitem folder only when the plan is workitem-worthy or a handoff is explicitly requested.
- `STRUCTURE` alone does not require a workitem folder.
- Reuse an existing workitem folder when the topic is clearly the same.
- Create a new workitem folder when the topic is new or the scope has materially changed.
- Use short lowercase `snake_case` slugs.

## Recommended Workitem Layout

```text
docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/
  debug/
  handoffs/
```
