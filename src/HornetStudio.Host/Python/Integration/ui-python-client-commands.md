# Python Client Commands

This document describes the predefined public commands of the bundled `ui_python_client` helper API.
It is the source of truth for the Python client command surface used by generated client scripts.

Rule:
Whenever the helper API changes, this file and the copied runtime doc in `src/HornetStudio.Editor/Templates/ui_python_client/COMMANDS.md` must be updated in the same change.

## Import

```python
from hornet_studio_host import host
from ui_python_client import FunctionResult, PythonClient
```

## hornet_studio_host

### Host Values

```python
from hornet_studio_host import host

temperature = host.values.temperature.value
host.values["Runtime.Test.Setpoint"].value = 100
```

Purpose:
- access the host-projected value view from Python

Behavior:
- attribute access uses generated aliases when available
- index access accepts either a visible alias or a projected registry path
- writes go back through the host bridge and remain host-administered
- projected registry paths may include runtime entries like `Studio.Dummy.Applications.Python.Raw.raw_b`

### Host Value Metadata

```python
from hornet_studio_host import host

value = host.values.temperature
unit = value.unit
path = value.path
is_writable = value.is_writable
```

Metadata fields:
- `alias`
- `path`
- `title`
- `unit`
- `format`
- `kind`
- `data_type`
- `is_writable`

### Host Log

```python
from hornet_studio_host import host

host.log.info("Python started")
host.log.warning("Temperature high")
```

Purpose:
- write log messages through the active host-managed Python client

## PythonClient

### Create Client

```python
client = PythonClient("LogTest", capabilities=["functions", "host_log"])
```

Parameters:
- `client_name`: visible name of the Python client instance
- `client_type`: optional protocol client type, default `ui-python-client`
- `capabilities`: optional capability list like `functions`, `host_log`, `values`
- `client_version`: optional client version string
- `api_version`: optional API version string

### Register Function

```python
@client.function("ping", description="Connectivity test", category="demo")
def ping(args: dict[str, object]) -> FunctionResult:
    return FunctionResult.ok(message="pong")
```

Purpose:
- registers a host-callable function

Arguments:
- `name`: function name visible to the host
- `description`: optional text shown to the host
- `category`: optional grouping label

Handler signatures:
- `def handler()`
- `def handler(args)`
- `def handler(args, message)`

### Init Hook

```python
@client.on_init
def handle_init() -> None:
    client.log_info("init received from host")
```

Purpose:
- runs after the host sends `init`
- use this for startup logs or local initialization

### Stop Hook

```python
@client.on_stop
def handle_stop() -> None:
    client.log_info("cleanup")
```

Purpose:
- runs when the host requests `stop`

### Heartbeat Hook

```python
@client.on_heartbeat
def handle_heartbeat() -> None:
    client.log_debug("heartbeat received")
```

Purpose:
- runs when the host sends `heartbeat`
- overrides the default debug log behavior

### Ready Payload

```python
client.set_ready_payload("LogTest is ready!")
```

Purpose:
- defines the payload sent with the `ready` message after initialization

### Register Value

```python
client.register_value("raw_a", title="LogTest/raw_a", unit="V")
```

Purpose:
- declares a value channel for the host

Arguments:
- `name`: value key
- `title`: optional display title
- `unit`: optional engineering unit

### Update Value

```python
client.update_value("raw_a", 12.34)
```

Purpose:
- sends a runtime value update to the host

Behavior:
- if the value name is unknown, it is auto-registered first

### Generic Log

```python
client.log("info", "message")
```

Purpose:
- sends a raw bridge log message with an explicit level

### Convenience Logs

```python
client.log_debug("debug")
client.log_info("info")
client.log_warning("warning")
client.log_error("error")
```

Purpose:
- shorthand helpers for the common log levels

### Exception Log

```python
try:
    raise RuntimeError("broken")
except Exception as exc:
    client.log_exception(exc, "operation failed")
```

Purpose:
- logs exceptions in a consistent error format

### Send Hello

```python
client.hello()
```

Purpose:
- manually sends the `hello` handshake frame

Note:
- `client.run()` already calls this automatically

### Send Ready

```python
client.ready("done")
```

Purpose:
- manually sends the `ready` frame

Note:
- `client.run()` sends this automatically after `init`

### Send Result

```python
client.result(request_id, FunctionResult.ok(payload={"sum": 3}))
```

Purpose:
- manually returns a structured host invocation result

Note:
- normal decorated function handlers usually do not need this directly

### Run Loop

```python
if __name__ == "__main__":
    raise SystemExit(client.run())
```

Purpose:
- starts the stdin/stdout bridge loop
- handles `init`, `invoke`, `stop`, and `heartbeat`

Editor support:
- generated Python client folders also receive a local `.vscode/settings.json`
- the workspace enables `python.analysis.extraPaths` for the script root and turns on auto-import completions
- `.vscode/extensions.json` recommends the VS Code extensions `ms-python.python` and `ms-python.vscode-pylance`
- generated folders also receive the `hornet_studio_host` package for projected host value access with local type information

## FunctionResult

### Success Result

```python
return FunctionResult.ok(payload={"sum": 3}, message="done")
```

### Failure Result

```python
return FunctionResult.fail("device not connected")
```

Purpose:
- standardized result container for host-invoked functions

Fields:
- `success`
- `message`
- `payload`

## Typical Example

```python
from ui_python_client import FunctionResult, PythonClient

client = PythonClient("LogTest", capabilities=["functions", "host_log"])
client.set_ready_payload("LogTest is ready!")


@client.on_init
def handle_init() -> None:
    client.log_info("initialized")


@client.function("ping", description="Connectivity test", category="demo")
def ping(args: dict[str, object]) -> FunctionResult:
    return FunctionResult.ok(payload={"echo": args}, message="pong")


if __name__ == "__main__":
    raise SystemExit(client.run())
```
