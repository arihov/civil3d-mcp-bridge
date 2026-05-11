"""Diagnostic tools + generic call-through escape hatch."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, as_json, call, mcp
from civil3d_mcp.models import GenericCall


@mcp.tool(name="civil3d_bridge_health",
          annotations={**READ_ANN, "title": "Bridge health check"})
async def civil3d_bridge_health() -> str:
    """Check the C# bridge is reachable. Returns its version + the full
    list of registered tool names — handy for spotting a Python/C# version
    mismatch (a typed wrapper here that doesn't exist on the bridge will
    show up as a missing entry in the bridge's tool list)."""
    from civil3d_mcp.bridge import BridgeError, health
    try:
        return as_json(await health())
    except BridgeError as exc:
        return as_json({"error": str(exc), "ok": False})


@mcp.tool(name="civil3d_call",
          annotations={**WRITE_ANN, "title": "Generic bridge call (escape hatch)"})
async def civil3d_call(params: GenericCall) -> str:
    """Call any tool registered with the C# bridge by name, with raw args.

    Use when the typed wrapper doesn't exist (yet) for some tool you saw
    in civil3d_bridge_health output. Args are passed through verbatim;
    schema validation happens server-side. Useful for newly-added bridge
    tools before the Python typed wrapper is updated."""
    return await call(params.tool, params.args)
