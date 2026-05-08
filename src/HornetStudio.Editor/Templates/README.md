# Python Templates

This folder contains reusable Python templates for the PythonClient widgets.

The generated Scripts folder also receives a bundled support package named `ui_python_client`.
Use that package from your scripts instead of hand-building JSON dictionaries.
It also receives a bundled support package named `hornet_studio_host` for projected host value access.
It also receives a command reference file for the predefined helper API.
It also receives `PYTHON_SYSTEM.md` with a compact system overview for editing in VS Code.
For better VS Code autocompletion, the generated Scripts folder also receives local `.vscode` settings and Python extension recommendations.

## Workflow

Use the `Template` button in the widget properties next to `Python Script`.
The selected template file is copied into the current folder's `Scripts` directory.
The copied file name is based on the widget name.
The helper package `ui_python_client` is copied alongside the script automatically.
The helper package `hornet_studio_host` is copied alongside the script automatically.

## Supported Placeholders

- `{{CLIENT_NAME}}`: display name of the Python client/widget
- `{{WIDGET_NAME}}`: widget name
- `{{SCRIPT_FILE_NAME}}`: created script file name
- `{{FOLDER_NAME}}`: current folder/page name

## Templates

### PythonClientDemo.py
General bridge demo with handshake, registered functions and structured results.
Use this as the default starting point for a full PythonClient.

### LogsTemplate.py
Shows how to send log messages through the bridge.
Includes `write_demo_logs`, `get_log_message`, and `write_host_log`.

### RegisterRawValuesRandom3Template.py
Shows how to declare three values with `define_value` and update them continuously with `value_update`.
Includes `start_loop` and `stop_loop`.

### RegisterFunctionLogEntryTemplate.py
Shows how to register a host-invokable function that writes a log entry.
Includes `write_log_entry`.

### HostValuesDemo.py
Shows how to inspect and write projected host values via `hornet_studio_host.host.values`.
Includes `list_host_values`, `read_host_value`, and `write_host_value`.

## Notes

- Value templates publish data via `define_value` and `value_update`.
- The host maps these messages into the registry under project-local paths like `studio.<Folder>.Applications.Python.<ClientName>.<ValueName>` when started from the UI. Legacy `project.<Folder>...` inputs are still resolved for compatibility.
- You can target these registry paths from signal widgets after the PythonClient is running.
- For simple user inputs or local computed values, prefer the `CustomSignals` widget over Python. Keep Python for integrations, external IO, or more complex logic.
- `LogsTemplate.py` can be used directly from `InteractionRules` with `InvokePythonClientFunction` or `InvokePythonFunction`; pass plain text like `Hello HostLog`.
- The helper package exposes typed functions like `PythonClient`, `log_info`, `register_value`, `update_value` and `@client.function(...)` for better VS Code autocompletion and debugging.
- The `hornet_studio_host` package exposes `host.values` and `host.log` for projected host registry access with local type information.
- Additional convenience hooks are available via `@client.on_init`, `@client.on_stop`, `@client.on_heartbeat`, `client.set_ready_payload(...)` and `client.log_exception(...)`.
- The copied file `ui_python_client/COMMANDS.md` documents the predefined public commands that generated Python clients can use.
- The copied file `PYTHON_SYSTEM.md` summarizes runtime flow, folder structure, and the expected `InteractionRules` argument shape.
