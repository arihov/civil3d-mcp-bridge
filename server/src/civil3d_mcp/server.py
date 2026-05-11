"""civil3d_mcp.server — FastMCP entry point.

This module's only jobs are (a) import the tools subpackage so every
@mcp.tool decorator runs against the shared mcp singleton, and (b) launch
the FastMCP run loop over stdio.
"""

from __future__ import annotations

import asyncio
import logging

from civil3d_mcp._instance import as_json, mcp
from civil3d_mcp.bridge import BridgeError, close as close_bridge, health
from civil3d_mcp.models import GenericCall
from civil3d_mcp import tools  # noqa: F401 — side-effect import registers all tools

log = logging.getLogger(__name__)


@mcp.tool(
    name="civil3d_bridge_health",
    annotations={
        "title": "Bridge health check",
        "readOnlyHint": True,
        "destructiveHint": False,
        "idempotentHint": True,
        "openWorldHint": True,
    },
)
async def civil3d_bridge_health() -> str:
    """Check the C# bridge is reachable. Returns ok status plus the list of
    tools the bridge currently has registered — useful for spotting a
    mismatch between Python server and add-in build versions."""
    try:
        return as_json(await health())
    except BridgeError as exc:
        return as_json({"ok": False, "error": str(exc)})


@mcp.tool(
    name="civil3d_call",
    annotations={
        "title": "Generic bridge call",
        "readOnlyHint": False,
        "destructiveHint": True,
        "idempotentHint": False,
        "openWorldHint": True,
    },
)
async def civil3d_call(params: GenericCall) -> str:
    """Escape hatch: call any registered bridge tool by name with raw args.

    Useful if a new tool ships in the C# add-in before its typed Python
    wrapper lands. Prefer the dedicated wrappers when they exist — they
    give Pydantic validation and clearer errors.
    """
    from civil3d_mcp._instance import call
    return await call(params.tool, params.args or {})


def main() -> None:
    """Entry point for the `civil3d-mcp` console script."""
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )
    try:
        mcp.run()
    finally:
        try:
            asyncio.run(close_bridge())
        except RuntimeError:
            pass


if __name__ == "__main__":
    main()
