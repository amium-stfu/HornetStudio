# Language and Naming

## Language Rules

- Always answer in German in chat.
- Always write code, comments, XML documentation, UI text, file names, and technical identifiers in English.
- Always write user-visible error and validation messages in English.

## Naming Rules

- Prefer named parameters for method calls instead of purely positional arguments, especially when there are more than two parameters, parameters have similar types, or the meaning is unclear.
- All newly created path-relevant names must use `snake_case`.
- Path-relevant names include folders, files, generated artifact names, slugs, and configuration values that are directly used as file system paths.
- Technical mapping files whose names are derived from runtime identifiers, persisted type values, public API names, or framework-required names are exempt from the `snake_case` path rule.
- Existing path names are preserved unless the requested task explicitly requires renaming them.
- C# identifiers, namespaces, project names, public APIs, and existing .NET naming conventions are not changed solely because of the `snake_case` path rule.
