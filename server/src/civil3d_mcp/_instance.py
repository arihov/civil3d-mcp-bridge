"""Singleton FastMCP instance and shared call-through helper.

Keeping the `mcp` instance in its own module lets every `tools/*.py` import
it without pulling in `server.py` (which would create a circular import,
because server.py also imports the tools modules to trigger registration).
"""

from __future__ import annotations

import json
from typing import Any

from mcp.server.fastmcp import FastMCP

mcp = FastMCP("civil3d_mcp")

# Shared annotation presets. All Civil 3D calls touch an "open world"
# (the live drawing) so openWorldHint is always true.
READ_ANN = {
    "readOnlyHint": True,
    "destructiveHint": False,
    "idempotentHint": True,
    "openWorldHint": True,
}

WRITE_ANN = {
    "readOnlyHint": False,
    "destructiveHint": True,
    "idempotentHint": False,
    "openWorldHint": True,
}


def as_json(payload: Any) -> str:
    """Stable JSON formatting for tool returns."""
    return json.dumps(payload, indent=2, default=str)


async def call(tool_name: str, args: dict[str, Any] | None = None) -> str:
    """Invoke a Civil 3D tool through the bridge, returning JSON.

    Imported lazily to avoid circular import (bridge.py imports nothing
    from us, but keeping the import here matches the lazy pattern other
    tool modules can follow).
    """
    from civil3d_mcp.bridge import BridgeError, invoke
    try:
        result = await invoke(tool_name, args)
        return as_json(result)
    except BridgeError as exc:
        return as_json({"error": str(exc), "tool": tool_name})
