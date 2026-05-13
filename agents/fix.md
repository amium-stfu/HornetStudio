# Fix Workflow

## FIX

- Use `FIX` for concrete, localized defects with clear expected behavior.
- Do not require a workitem for `FIX`.
- Do not create debug reports, handoffs, or workitem folders.
- First identify the likely cause briefly before changing code.
- Implement only the smallest targeted correction needed to restore expected behavior.
- Keep changes limited to the affected code path and directly related tests or documentation.
- Do not add features, broad refactorings, structural changes, or speculative abstractions.
- Do not change public APIs unless the fix requires it and the impact is explicitly reported.
- Run the narrowest reliable validation that can prove the fix.
- Report the cause, changed files, and validation result concisely.

## Escalation to DEBUG

- Stop `FIX` and ask to switch to `DEBUG` with a known workitem when the issue becomes unclear, broad, recurring, high-risk, or multi-step.
- Stop `FIX` when the expected behavior is not clear enough to validate.
- Stop `FIX` when the likely fix requires repeated speculative attempts.
- Stop `FIX` when the defect spans multiple subsystems or requires durable debug history.
