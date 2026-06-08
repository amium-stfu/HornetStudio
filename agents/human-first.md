# Human-First Code Style

## Goal

- Write code like an experienced human maintainer.
- Optimize first for readability, local clarity, and maintainability.
- Prefer code that a developer can understand in one pass.

## Simplicity First

- Prefer the simplest solution that solves the requested problem.
- Avoid overengineering.
- Avoid speculative future-proofing.
- Do not add infrastructure for features that do not exist yet.
- Do not implement theoretical future requirements.
- Do not introduce generic frameworks when a direct implementation is sufficient.

## Control Flow and Abstraction

- Prefer direct, readable control flow over additional abstraction.
- Do not create helper methods unless they remove duplication or the extracted name clearly improves understanding.
- Do not split simple logic into many small methods.
- Prefer one clear method with simple branches over many tiny methods when the result is easier to understand.
- Keep related logic close together.
- Preserve the existing mental model of the code.

## Scope and Restraint

- Keep the first implementation minimal and domain-focused.
- Match the style of the surrounding code.
- Do not make code more abstract than the existing implementation.
- Do not rewrite working code just to fit a preferred pattern.
- Prefer consistency with the repository over theoretical best practices.

## Avoid Typical AI Patterns

Unless explicitly required:

- Do not add DTOs.
- Do not add wrapper classes.
- Do not add compatibility layers.
- Do not add fallback layers.
- Do not add defensive cloning.
- Do not add caching layers.
- Do not add event systems.
- Do not add summary publishing.
- Do not add extension points.
- Do not add configuration systems.
- Do not add dependency injection.
- Do not add generic infrastructure.
- Do not add services, managers, providers, factories, adapters, or registries unless there is a clear technical benefit.

## Readability

- Use clear names.
- Prefer readable code over clever code.
- Avoid deeply nested logic where possible.
- Keep methods reasonably small.
- A longer method is acceptable if it is easier to understand than many fragmented helper methods.

## Error Handling

- Handle realistic error cases.
- Avoid excessive defensive programming.
- Do not add validation for impossible states.
- Do not add exception handling solely to make the code look safer.
- Fail clearly when invalid input would indicate a programming error.
