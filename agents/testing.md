# Testing and Validation

## Validation Preference Order

- Prefer unit tests first.
- Then prefer integration tests.
- Then prefer lightweight self-test hosts.
- Then prefer simulation environments.
- Then prefer loopback tests.
- Prefer reproducible verification workflows over manual spot checks.
- Use the narrowest verification path that can falsify the current hypothesis.

## Quality Rules

- Create or update appropriate unit tests for new or changed logic.
- Handle errors explicitly and clearly.
- Do not use silent `catch` blocks.
- Use meaningful logging for errors and important workflows.
- Exceptions should be descriptive and include relevant context.
- Use `async` and `await` correctly and avoid blocking calls such as `.Result` or `.Wait()`.
- Use nullable reference types deliberately and avoid unnecessary null risks.
- Follow existing `.editorconfig` and formatting rules.

## Build Verification

- A build check means building the complete solution without starting any application.
- Prefer `dotnet build HornetStudio.sln --no-restore` for build verification when restore has already completed.
- Do not use `dotnet run` or start demo, UI, or service projects as part of a build check.