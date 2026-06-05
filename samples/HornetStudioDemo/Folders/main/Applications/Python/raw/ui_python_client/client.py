from __future__ import annotations

import inspect
import json
import sys
import traceback
from typing import Any, Callable, Iterable, TextIO

from .types import FunctionResult, JsonDict, ValueDefinition

try:
    from hornet_studio_host.runtime import apply_host_value_update, configure_host_bridge
except ImportError:  # pragma: no cover - optional during incremental rollout
    def configure_host_bridge(init_payload: JsonDict, client: "PythonClient") -> None:
        return None

    def apply_host_value_update(payload: JsonDict) -> None:
        return None

FunctionHandler = Callable[..., Any]


def _build_exception_payload(exc: Exception) -> JsonDict:
    traceback_entries = traceback.extract_tb(exc.__traceback__)
    last_frame = traceback_entries[-1] if traceback_entries else None
    return {
        "exception_type": type(exc).__name__,
        "message": str(exc),
        "file": last_frame.filename if last_frame else None,
        "line": last_frame.lineno if last_frame else None,
        "function": last_frame.name if last_frame else None,
        "traceback": "".join(traceback.format_exception(type(exc), exc, exc.__traceback__)),
    }


class PythonClient:
    bridge_version = "1.0"

    def __init__(
        self,
        client_name: str,
        *,
        client_type: str = "ui-python-client",
        capabilities: Iterable[str] | None = None,
        client_version: str = "0.1.0",
        api_version: str = "1.0",
        stdin: TextIO | None = None,
        stdout: TextIO | None = None,
    ) -> None:
        """Create a typed client bridge for host-managed Python scripts.

        Args:
            client_name: Human-readable client name shown to the host.
            client_type: Protocol client type identifier.
            capabilities: Advertised bridge features such as "functions" or "values".
            client_version: Version string of the user script.
            api_version: Bridge API version expected by the client.
            stdin: Optional input stream for tests.
            stdout: Optional output stream for tests.
        """
        self.client_name = client_name
        self.client_type = client_type
        self.capabilities = list(capabilities or ["functions", "host_log"])
        self.client_version = client_version
        self.api_version = api_version
        self._stdin = stdin or sys.stdin
        self._stdout = stdout or sys.stdout
        self._functions: dict[str, tuple[JsonDict, FunctionHandler]] = {}
        self._values: dict[str, ValueDefinition] = {}
        self._init_handlers: list[Callable[[], None]] = []
        self._stop_handlers: list[Callable[[], None]] = []
        self._heartbeat_handlers: list[Callable[[], None]] = []
        self._ready_payload: Any = None
        self._initialized = False

    def function(
        self,
        name: str,
        *,
        description: str = "",
        category: str = "demo",
    ) -> Callable[[FunctionHandler], FunctionHandler]:
        """Register a host-callable function via decorator.

        The decorated handler may accept no arguments, one `args` argument,
        or `args` plus the raw bridge message.
        """
        def decorator(handler: FunctionHandler) -> FunctionHandler:
            self._functions[name] = (
                {
                    "name": name,
                    "description": description,
                    "category": category,
                },
                handler,
            )
            return handler

        return decorator

    def on_init(self, handler: Callable[[], None]) -> Callable[[], None]:
        """Register a callback that runs after the host sends `init`."""
        self._init_handlers.append(handler)
        return handler

    def on_stop(self, handler: Callable[[], None]) -> Callable[[], None]:
        """Register a callback that runs when the host requests shutdown."""
        self._stop_handlers.append(handler)
        return handler

    def on_heartbeat(self, handler: Callable[[], None]) -> Callable[[], None]:
        """Register a callback for host heartbeat messages."""
        self._heartbeat_handlers.append(handler)
        return handler

    def set_ready_payload(self, payload: Any) -> None:
        """Set the payload sent with the automatic `ready` response."""
        self._ready_payload = payload

    def register_value(self, name: str, *, title: str | None = None, unit: str | None = None) -> ValueDefinition:
        """Declare a value channel that can later be updated from the script."""
        definition = ValueDefinition(name=name, title=title, unit=unit)
        self._values[name] = definition
        if self._initialized:
            self._send_define_value(definition)
        return definition

    def update_value(self, name: str, value: Any) -> None:
        """Publish a runtime value update to the host.

        Unknown values are auto-registered with default metadata.
        """
        if name not in self._values:
            self.register_value(name)

        self._send(
            {
                "type": "value_update",
                "payload": {
                    "name": name,
                    "value": value,
                },
            }
        )

    def log(self, level: str, message: str) -> None:
        """Send a raw log message with an explicit level."""
        self._send(
            {
                "type": "log",
                "payload": {
                    "level": level,
                    "message": message,
                },
            }
        )

    def log_debug(self, message: str) -> None:
        """Write a debug log entry to the host log window."""
        self.log("debug", message)

    def log_info(self, message: str) -> None:
        """Write an informational log entry to the host log window."""
        self.log("info", message)

    def log_warning(self, message: str) -> None:
        """Write a warning log entry to the host log window."""
        self.log("warning", message)

    def log_error(self, message: str) -> None:
        """Write an error log entry to the host log window."""
        self.log("error", message)

    def log_exception(self, exc: Exception, message: str | None = None) -> None:
        """Write an exception as a formatted error log entry."""
        if message:
            self.log_error(f"{message}: {exc}")
            return

        self.log_error(f"{type(exc).__name__}: {exc}")

    def hello(self) -> None:
        """Send the initial handshake frame to the host."""
        self._send(
            {
                "type": "hello",
                "payload": {
                    "client_name": self.client_name,
                    "client_type": self.client_type,
                    "capabilities": self.capabilities,
                    "client_version": self.client_version,
                    "api_version": self.api_version,
                },
            }
        )

    def ready(self, payload: Any = None) -> None:
        """Send a `ready` frame to the host."""
        self._send(
            {
                "type": "ready",
                "payload": payload,
            }
        )

    def result(self, request_id: Any, result: FunctionResult) -> None:
        """Send a structured result for a host-triggered invocation."""
        self._send(
            {
                "type": "result",
                "request_id": request_id,
                "payload": {
                    "success": result.success,
                    "message": result.message,
                    "payload": result.payload,
                },
            }
        )

    def send_host_value_write(self, path: str, value: Any) -> None:
        """Write a projected host value back into the host registry."""
        self._send(
            {
                "type": "host_value_write",
                "payload": {
                    "path": path,
                    "value": value,
                },
            }
        )

    def run(self) -> int:
        """Start the bridge loop and process host messages until shutdown."""
        self.hello()

        for raw_line in self._stdin:
            line = raw_line.strip()
            if not line:
                continue

            try:
                message = json.loads(line)
            except json.JSONDecodeError as exc:
                self.log_error(f"Failed to decode message: {exc}: {line!r}")
                continue

            if not self._handle_message(message):
                break

        return 0

    def _handle_message(self, message: JsonDict) -> bool:
        msg_type = message.get("type")

        if msg_type == "init":
            self._initialized = True
            configure_host_bridge(message.get("payload") or {}, self)
            self._announce_definitions()
            for handler in self._init_handlers:
                try:
                    handler()
                except Exception as exc:  # noqa: BLE001
                    self.log_exception(exc, "Exception during init handler")
                    raise
            self.ready(self._ready_payload)
            return True

        if msg_type == "invoke":
            self._handle_invoke(message)
            return True

        if msg_type == "stop":
            self.log_info("stop requested by host; shutting down")
            for handler in self._stop_handlers:
                try:
                    handler()
                except Exception as exc:  # noqa: BLE001
                    self.log_exception(exc, "Exception during stop handler")
                    raise
            return False

        if msg_type == "heartbeat":
            if self._heartbeat_handlers:
                for handler in self._heartbeat_handlers:
                    try:
                        handler()
                    except Exception as exc:  # noqa: BLE001
                        self.log_exception(exc, "Exception during heartbeat handler")
                        raise
            else:
                self.log_debug("heartbeat received")
            return True

        if msg_type == "host_value_update":
            apply_host_value_update(message.get("payload") or {})
            return True

        self.log_debug(f"Unhandled message type: {msg_type}")
        return True

    def _announce_definitions(self) -> None:
        for definition, _ in self._functions.values():
            self._send(
                {
                    "type": "define_function",
                    "payload": definition,
                }
            )

        for definition in self._values.values():
            self._send_define_value(definition)

    def _send_define_value(self, definition: ValueDefinition) -> None:
        self._send(
            {
                "type": "define_value",
                "payload": {
                    "name": definition.name,
                    "title": definition.title or f"{self.client_name}/{definition.name}",
                    "unit": definition.unit,
                },
            }
        )

    def _handle_invoke(self, message: JsonDict) -> None:
        request_id = message.get("request_id")
        payload = message.get("payload") or {}
        function_name = str(payload.get("function_name") or "").strip()
        args = payload.get("args") or {}

        definition_and_handler = self._functions.get(function_name)
        if definition_and_handler is None:
            self.result(request_id, FunctionResult.fail(f"Unknown function '{function_name}'"))
            return

        _, handler = definition_and_handler
        try:
            response = self._call_handler(handler, args, message)
            self.result(request_id, self._normalize_result(response))
        except Exception as exc:  # noqa: BLE001
            self.log_exception(exc, f"Exception during '{function_name}'")
            self.result(
                request_id,
                FunctionResult.fail(
                    f"Exception during '{function_name}'",
                    _build_exception_payload(exc),
                ),
            )

    @staticmethod
    def _call_handler(handler: FunctionHandler, args: JsonDict, message: JsonDict) -> Any:
        parameter_count = len(inspect.signature(handler).parameters)
        if parameter_count <= 0:
            return handler()
        if parameter_count == 1:
            return handler(args)
        return handler(args, message)

    @staticmethod
    def _normalize_result(result: Any) -> FunctionResult:
        if isinstance(result, FunctionResult):
            return result
        if result is None:
            return FunctionResult.ok()
        if isinstance(result, str):
            return FunctionResult.ok(message=result)
        return FunctionResult.ok(payload=result)

    def _send(self, message: JsonDict) -> None:
        frame = dict(message)
        frame.setdefault("bridge_version", self.bridge_version)
        self._stdout.write(json.dumps(frame, separators=(",", ":")) + "\n")
        self._stdout.flush()