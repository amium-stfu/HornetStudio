from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict

JsonDict = Dict[str, Any]


@dataclass(slots=True)
class FunctionResult:
    """Structured result payload for host-invoked Python functions."""

    success: bool
    message: str | None = None
    payload: Any = None

    @classmethod
    def ok(cls, payload: Any = None, message: str | None = None) -> "FunctionResult":
        """Create a successful function result."""
        return cls(success=True, message=message, payload=payload)

    @classmethod
    def fail(cls, message: str, payload: Any = None) -> "FunctionResult":
        """Create a failed function result."""
        return cls(success=False, message=message, payload=payload)


@dataclass(slots=True)
class ValueDefinition:
    """Metadata for a value channel published to the host."""

    name: str
    title: str | None = None
    unit: str | None = None