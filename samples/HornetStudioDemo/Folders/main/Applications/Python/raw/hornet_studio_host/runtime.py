from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Iterable


class HostLog:
    def __init__(self, runtime: "HostRuntime") -> None:
        self._runtime = runtime

    def debug(self, message: str) -> None:
        self._runtime._log("debug", message)

    def info(self, message: str) -> None:
        self._runtime._log("info", message)

    def warning(self, message: str) -> None:
        self._runtime._log("warning", message)

    def error(self, message: str) -> None:
        self._runtime._log("error", message)


@dataclass(slots=True)
class HostValue:
    runtime: "HostRuntime"
    alias: str
    path: str
    title: str | None = None
    unit: str | None = None
    format: str | None = None
    kind: str | None = None
    data_type: str = "unknown"
    is_writable: bool = True
    _value: Any = None

    @property
    def value(self) -> Any:
        return self._value

    @value.setter
    def value(self, new_value: Any) -> None:
        if not self.is_writable:
            raise RuntimeError(f"Host value '{self.path}' is read-only")

        self.runtime._write_value(self.path, new_value)
        self._value = new_value


class HostValues:
    def __init__(self, runtime: "HostRuntime") -> None:
        self._runtime = runtime
        self._by_alias: dict[str, HostValue] = {}
        self._by_path: dict[str, HostValue] = {}

    def __getitem__(self, key: str) -> HostValue:
        if key in self._by_alias:
            return self._by_alias[key]
        if key in self._by_path:
            return self._by_path[key]
        raise KeyError(f"Unknown host value '{key}'")

    def __getattr__(self, name: str) -> HostValue:
        try:
            return self._by_alias[name]
        except KeyError as exc:
            raise AttributeError(name) from exc

    def __dir__(self) -> list[str]:
        return sorted(set(super().__dir__()) | set(self._by_alias.keys()))

    def __iter__(self):
        return iter(self._by_alias.values())

    def keys(self) -> list[str]:
        return list(self._by_alias.keys())

    def paths(self) -> list[str]:
        return list(self._by_path.keys())

    def values(self) -> list[HostValue]:
        return list(self._by_alias.values())

    def items(self) -> list[tuple[str, HostValue]]:
        return list(self._by_alias.items())

    def _replace_all(self, definitions: Iterable[dict[str, Any]]) -> None:
        self._by_alias.clear()
        self._by_path.clear()

        for definition in definitions:
            alias = str(definition.get("alias") or "value")
            path = str(definition.get("path") or alias)
            value = HostValue(
                runtime=self._runtime,
                alias=alias,
                path=path,
                title=_as_optional_string(definition.get("title")),
                unit=_as_optional_string(definition.get("unit")),
                format=_as_optional_string(definition.get("format")),
                kind=_as_optional_string(definition.get("kind")),
                data_type=str(definition.get("data_type") or "unknown"),
                is_writable=bool(definition.get("is_writable", True)),
                _value=definition.get("value"),
            )
            self._by_alias[alias] = value
            self._by_path[path] = value

    def _apply_update(self, payload: dict[str, Any]) -> None:
        path = str(payload.get("path") or "")
        alias = str(payload.get("alias") or "")

        target = self._by_path.get(path)
        if target is None and alias:
            target = self._by_alias.get(alias)

        if target is None:
            return

        target._value = payload.get("value")


class HostRuntime:
    def __init__(self) -> None:
        self.values = HostValues(self)
        self.log = HostLog(self)
        self._client: Any = None

    @property
    def is_connected(self) -> bool:
        return self._client is not None

    def _configure(self, init_payload: dict[str, Any], client: Any) -> None:
        self._client = client
        self.values._replace_all(init_payload.get("host_values") or [])

    def _write_value(self, path: str, value: Any) -> None:
        if self._client is None:
            raise RuntimeError("hornet_studio_host is not connected to a running PythonClient")

        self._client.send_host_value_write(path, value)

    def _log(self, level: str, message: str) -> None:
        if self._client is None:
            return
        self._client.log(level, message)


def _as_optional_string(value: Any) -> str | None:
    if value is None:
        return None
    text = str(value)
    return text or None


host = HostRuntime()


def configure_host_bridge(init_payload: dict[str, Any], client: Any) -> None:
    host._configure(init_payload, client)


def apply_host_value_update(payload: dict[str, Any]) -> None:
    host.values._apply_update(payload)