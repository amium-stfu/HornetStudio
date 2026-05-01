# Python System Overview

This document describes how the Python client and Python application system is structured in HornetStudio.
It is the source of truth for the Python bridge/runtime overview used by generated script folders.

Rule:
Whenever the Python bridge, template workflow, interaction argument shape, or generated support files change, update this file and the copied runtime doc in `src/HornetStudio.Editor/Templates/PYTHON_SYSTEM.md` in the same change.

## Purpose

- Python scripts are edited in VS Code.
- The host starts and stops Python scripts.
- Scripts talk to the host over the bundled `ui_python_client` bridge.
- Templates provide very small starter files for common use cases.

## Main Areas

- `src/HornetStudio.Host/Python/Client/`: host-side runtime bridge for Python clients.
- `src/HornetStudio.Host/Python/Integration/`: source-of-truth docs for the Python bridge.
- `src/HornetStudio.Editor/Templates/`: starter templates copied for widgets and Python applications.
- `src/HornetStudio.Editor/Templates/hornet_studio_host/`: bundled host-value access package copied with generated scripts.
- `src/HornetStudio.Editor/Templates/ui_python_client/`: bundled helper package copied with generated scripts.
- `src/HornetStudio.Editor/Widgets/ApplicationExplorer/`: editor/runtime UI for Python applications, surfaced in the UI as `ApplicationExplorer`.

## Generated Folder Layout

When a Python script or Python application is created, the target folder receives:

- the selected `.py` template
- `PYTHON_SYSTEM.md`
- `.vscode/settings.json`
- `.vscode/extensions.json`
- `hornet_studio_host/`
- `ui_python_client/`
- `ui_python_client/COMMANDS.md`

This gives the script a local description of the system plus the concrete helper API reference.

Python applications created through `ApplicationExplorer` now live under:

- `<Folder.Directory>/Applications/Python/<ApplicationName>/`

Legacy folders under `<Folder.Directory>/Python/` may still exist and remain supported for migration compatibility.

## Runtime Model

- A Python script creates `PythonClient(...)`.
- The script calls `client.run()`.
- The host and script exchange JSON messages over stdin/stdout.
- The host can invoke registered Python functions.
- The script can publish logs and values back to the host.
- The host can project visible host registry values into Python as `hornet_studio_host.host.values`.
- Python function failures can return structured diagnostics to the host, including traceback text and, when available, file, function, and line information.

## Host Value Access

- Use `from hornet_studio_host import host` for projected host value access.
- Read values via `host.values.some_alias.value` or `host.values["Some.Registry.Path"].value`.
- Write values back with `host.values.some_alias.value = 42`.
- The host remains the source of truth and the only administrative authority.
- Projected paths may also include runtime values such as `Studio.Dummy.Applications.Python.Raw.raw_b`.

Available projected metadata may include:

- `alias`
- `path`
- `title`
- `unit`
- `format`
- `kind`
- `data_type`
- `is_writable`

## Interaction Rules

Two Python-related actions exist in the UI:

- `InvokePythonClientFunction`
- `InvokePythonFunction`

Both actions call a registered Python function by name.

Argument behavior:

- if the InteractionRules argument is empty, Python receives `{}`
- if the argument is valid JSON object/array, Python receives that parsed JSON
- if the argument is valid JSON scalar or plain text, Python receives `{ "value": ... }`

For simple templates, prefer reading `args.get("value")`.

## Template Guidelines

- Keep templates small and obvious.
- Prefer one small helper function plus one decorated host-callable function.
- Avoid unnecessary abstractions in templates.
- Use `client.log_info(...)` for visible behavior when useful.
- Use the bundled `ui_python_client` package instead of building raw JSON messages by hand.
- Use the bundled `hornet_studio_host` package for projected host value access.

## Editing Guidance

- Put reusable bridge/API behavior into `ui_python_client` or `hornet_studio_host`.
- Keep template files focused on the specific demo/use case.
- If the public helper API changes, also update:
  - `src/HornetStudio.Host/Python/Integration/ui-python-client-commands.md`
  - `src/HornetStudio.Editor/Templates/ui_python_client/COMMANDS.md`
- If the system behavior or generated folder structure changes, also update:
  - `src/HornetStudio.Host/Python/Integration/python-system-overview.md`
  - `src/HornetStudio.Editor/Templates/PYTHON_SYSTEM.md`
