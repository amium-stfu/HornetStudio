# Debugging Workflow

## Workitem Requirement

- Use `DEBUG` for unclear, broad, recurring, high-risk, or multi-step defect analysis.
- Before debugging, verify that the matching workitem is known.
- A workitem is known only when a path under `docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/` is open or mentioned, a handoff from that workitem is open or mentioned, the user explicitly names the workitem, or exactly one existing workitem clearly matches the problem.
- If no workitem is known, do not start debugging and do not create debug files.
- Instead, ask the user to open or name the matching handoff file, to switch to `PLAN` if a new workitem should be created, or to switch to `FIX` if the issue is concrete and localized.
- If multiple workitems could match, ask the user to choose the correct workitem before continuing.

## Analysis Order

- After identifying the workitem and before new analysis, read the existing files in that workitem's `debug/` folder.
- Use previous debug reports to avoid repeating already disproven hypotheses, failed fixes, or analysis loops.
- First analyze and explain the root cause.
- Do not jump directly to code without explaining the issue.
- Then propose a targeted solution.
- Implement targeted code changes when they are necessary to verify or fix the identified issue.
- Keep changes limited to the diagnosed issue and the validation needed to prove it.

## Validation Rules

- Prefer reproducible validation over subjective or ad-hoc checks.
- Use the narrowest reliable reproduction or verification path that can prove the issue and the fix.
- Prefer a measurable failing check before changing code whenever a lightweight reproduction exists.
- Re-run the same focused validation after each targeted fix to confirm actual progress.
- Count each autonomous code or configuration change that is intended to fix the issue as one fix attempt.
- Prefer returning to the last known stable state over accumulating speculative workaround chains.
- When a fix does not improve the measured result, treat that attempt as disproven and update the working hypothesis.

## Stop Conditions

- Do not continue speculative autonomous fix loops without confirmed progress.
- Stop after 5 consecutive autonomous fix attempts without measurable progress.
- If repeated attempts do not improve the failing validation, stop, summarize the disproven paths, and surface the remaining uncertainty.
- If validation becomes ambiguous, reduce scope to the smallest reproducible case before continuing.
- Create or update a handoff only when the issue cannot be completed within the stop conditions or requires follow-up work outside the current debug scope.

## Debug File Rules

- Create or update debug files only inside the identified workitem's `debug/` folder.
- Do not use a global `Debug.md` for new debugging work.

## Required DEBUG Structure

- Store debug reports in `docs/workitems/<yyyy.MM.dd.HHmm>-<slug>/debug/`.
- Use timestamped file names: `<yyyy.MM.dd.HHmm>-debug.md`.
- Keep only relevant, current debugging history.
- Remove outdated or disproven attempts when updating a debug report.

## Required DEBUG REPORT Structure

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
