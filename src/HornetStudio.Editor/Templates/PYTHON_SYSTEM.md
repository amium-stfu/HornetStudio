# Python System Overview

This file is copied into generated Python script and Python application folders.
It gives a compact overview of how the local script fits into the host system.

## What This Folder Contains

- your Python script
- `PYTHON_SYSTEM.md`
- `.vscode/` settings for Python editing
- `hornet_studio_host/`
- `ui_python_client/`
- `ui_python_client/COMMANDS.md`

When created from `ApplicationExplorer`, Python applications are stored under `Applications/Python/<ApplicationName>/`.

## How The Script Runs

- The host starts the script.
- The script creates `PythonClient(...)`.
- The script calls `client.run()`.
- The host and script exchange JSON messages over stdin/stdout.
- The host may project visible host registry values into Python as `hornet_studio_host.host.values`.
- Python function failures can send structured diagnostics back to the host, including traceback text and, when available, file, function, and line information.

## What The Script Can Do

- register callable functions with `@client.function(...)`
- write to the host log with `client.log_info(...)` and related helpers
- publish values with `client.register_value(...)` and `client.update_value(...)`
- read or write projected host values with `from hornet_studio_host import host`

## Host Value Access

- read with `host.values.some_alias.value`
- read by path with `host.values["Some.Registry.Path"].value`
- write with `host.values.some_alias.value = 42`
- inspect metadata like `unit`, `path`, and `is_writable`
- projected paths may also include runtime values such as `studio.Dummy.Applications.Python.Raw.raw_b`

## InteractionRules Arguments

When the host calls a Python function from `InteractionRules`:

- empty argument becomes `{}`
- JSON object/array stays JSON
- plain text becomes `{ "value": "..." }`

For simple templates, read `args.get("value")`.

## Keep Templates Simple

- prefer small functions
- avoid unnecessary helper layers
- use visible log output while testing

## Where To Look Next

- `ui_python_client/COMMANDS.md`: helper API reference
- `ui_python_client/client.py`: helper implementation
- `hornet_studio_host/runtime.py`: projected host value runtime helper
