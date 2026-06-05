from typing import Any


class HostValue:
    alias: str
    path: str
    title: str | None
    unit: str | None
    format: str | None
    kind: str | None
    data_type: str
    is_writable: bool

    @property
    def value(self) -> Any: ...

    @value.setter
    def value(self, new_value: Any) -> None: ...


class HostValues:
    def __getitem__(self, key: str) -> HostValue: ...
    def __getattr__(self, name: str) -> HostValue: ...
    def keys(self) -> list[str]: ...
    def paths(self) -> list[str]: ...
    def values(self) -> list[HostValue]: ...
    def items(self) -> list[tuple[str, HostValue]]: ...


class HostLog:
    def debug(self, message: str) -> None: ...
    def info(self, message: str) -> None: ...
    def warning(self, message: str) -> None: ...
    def error(self, message: str) -> None: ...


class HostRuntime:
    values: HostValues
    log: HostLog

    @property
    def is_connected(self) -> bool: ...


host: HostRuntime