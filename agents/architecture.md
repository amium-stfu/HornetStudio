# Architecture and Structure

## Architectural Evaluation

- Critically evaluate proposed structural changes before accepting them.
- Explicitly assess whether the idea is useful, necessary, maintainable, performant, and aligned with the existing project architecture.
- Point out risks, problematic assumptions, unnecessary complexity, performance costs, coupling, migration effort, and possible regressions.
- Prefer simpler alternatives when they solve the same problem with less complexity or lower risk.

## Structural Constraints

- Prefer a minimally fragmented architecture.
- Encapsulate and provide functions within their respective components rather than requiring additional separate helper components or intermediary signals unless technically required.
- Prefer extending existing components over introducing new classes, services, or wrapper layers without clear benefit.
- Follow the existing architecture, patterns, and naming conventions.
- Keep code readable, maintainable, and clearly structured for humans.
- Prefer descriptive names, small methods, and focused classes.
- Avoid magic numbers and duplication.
- Avoid overengineering and unnecessary abstractions.
- Do not introduce unnecessary comments, placeholder code, dead code, or speculative abstractions.

## Scalable Simplicity

- Design structures so they can grow when a concrete requirement appears, but do not implement abstractions for hypothetical future scenarios.
- Prefer a stable core protocol and clear boundaries over speculative extension points.
- Keep the current proven path simple, fast, and observable before adding optional layers.
- Add new abstraction layers only when there are at least two real use cases or a clear technical pressure point.
- Prefer localized future changes over upfront complexity that affects every current caller.

## Repository Structure

- Create a clean folder structure when topics can be clearly separated.
- When creating a solution, use a `./src` directory structure.
- Create a dedicated directory for each project inside `./src`.
- Group related classes, derived types, functions, and resources meaningfully.
- Avoid unnecessary fragmentation when fewer files make the structure easier to read.
