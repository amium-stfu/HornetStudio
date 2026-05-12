# Release and Versioning

## Versioning and Releases

- Every release version must have a unique version number.
- The version number is based on a timestamp in the format `yyyy.MM.dd.HHmm`, for example `2026.04.27.1430`.
- Generate version numbers only for releases, not for local builds.
- Every release version must be unique and monotonically increasing.
- There must be exactly one version per release as the single source of truth.
- Use version numbers consistently in code, packages, Git tags, and documentation.
- For every release, update `CHANGELOG.md` and relevant documentation.

## Publish

- Publish output must be written to `./Release`.
- Publish builds must be self-contained.
- Publish builds must use single-file output.
- For .NET projects, use `SelfContained=true` and `PublishSingleFile=true`.

## Assembly / NuGet

- Use the version number without a prefix, for example `2026.04.27.1430`.
- Use this version in project files, assembly metadata, and NuGet packages.

## Git and Security

- Create an appropriate `.gitignore` if none exists.
- Do not commit secrets, tokens, passwords, or machine-specific local paths.
- Change public APIs only when necessary, and point out breaking changes.
- Do not introduce new dependencies without justification.
- When adding runtime dependencies, briefly check license and FOSS relevance.

## Git Tags

- Use a leading `v` for Git tags, for example `v2026.04.27.1430`.

## FOSS and Notice Documentation

- Maintain a notice file with runtime FOSS components for releases.
- Do not include test or development dependencies in the notice file unless they are required at runtime.
- The notice file should include name, version, copyright information, license type, and full license texts.