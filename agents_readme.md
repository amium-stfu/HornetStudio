# Agents Rule System

This repository uses a small modular agent rule system.

## Structure

- `AGENTS.md` is the entry point. It defines priority rules, mode syntax, loading order, and mode-to-module routing.
- `agents/*.md` contains reusable workflow modules.
- `agents/solution.md` contains repository-specific rules and is always applied last.

All modules except `agents/solution.md` should stay solution-neutral so the rule system can be reused as a template in other repositories.

## Modes

- `ASK`: Answer only the question. No code and no implementation.
- `STRUCTURE`: Evaluate architecture, structure, responsibilities, boundaries, and risks. No implementation.
- `PLAN`: Create a clear implementation plan. Small plans stay in chat; larger plans can create a workitem and handoff.
- `TODO`: Create a concise backlog entry for non-urgent bugs, technical debt, or follow-up work.
- `IMPLEMENT`: Implement only the requested functionality with minimal, targeted changes.
- `FIX`: Fix a concrete, localized defect with clear expected behavior.
- `DEBUG`: Analyze unclear, broad, recurring, high-risk, or multi-step defects using a known workitem.
- `CLEAN`: Clean up existing code without changing intended behavior.
- `BUILD`: Build the solution without starting applications.
- `PUBLISH`: Create a release publish using the release rules.

## Mode Syntax

Preferred syntax:

```text
[MODE: IMPLEMENT]
```

Short syntax:

```text
#impl
```

Mode commands are recognized only when they are the first non-empty token in the user message.

## Workflow Example

1. Ask a question:

   ```text
   #ask
   How should this feature fit into the existing architecture?
   ```

2. Evaluate structure:

   ```text
   #struct
   Evaluate whether the new import pipeline should be a separate service or part of the existing importer.
   ```

3. Create a plan:

   ```text
   #plan
   Plan the implementation for the selected import pipeline approach.
   ```

4. Implement the planned change:

   ```text
   #impl
   Implement the planned import pipeline changes.
   ```

5. Fix a localized defect:

   ```text
   #fix
   The import preview does not refresh after selecting a new file. Expected: preview updates immediately.
   ```

6. Debug a broader issue:

   ```text
   #debug
   Continue debugging the failing import pipeline workitem.
   ```

7. Clean up without changing behavior:

   ```text
   #clean
   Clean up unused import preview code without changing runtime behavior.
   ```

8. Build the solution:

   ```text
   #build
   Build the solution and report warnings or errors.
   ```

9. Publish a release:

   ```text
   #publish
   Create a release publish for the current version.
   ```

## Recommended Use

- Use `ASK` for clarification and quick answers.
- Use `STRUCTURE` before larger architectural or structural changes.
- Use `PLAN` when the task needs ordering, scope control, or a handoff.
- Use `IMPLEMENT` only when the requested behavior is clear enough to build.
- Use `FIX` for small reproducible defects.
- Use `DEBUG` when the cause is unclear or repeated attempts are likely.
- Use `CLEAN` for behavior-preserving cleanup.
- Use `BUILD` for verification without starting applications.
- Use `PUBLISH` only for release publishing.
