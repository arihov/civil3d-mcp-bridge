"""Diagnostics, multi-session control and the generic call-through escape hatch."""

from __future__ import annotations

from civil3d_mcp import bridge
from civil3d_mcp._instance import READ_ANN, WRITE_ANN, as_json, call, mcp
from civil3d_mcp.models import (
    ActivateDocument,
    GenericCall,
    SelectDocument,
    SelectInstance,
    UndoSteps,
)


@mcp.tool(name="civil3d_bridge_health", annotations={**READ_ANN, "title": "Bridge health check"})
async def civil3d_bridge_health() -> str:
    """Check the bridge for the selected Civil 3D session: bridge version, Civil 3D
    release, .NET runtime, open drawings, busy state and the registered tool list."""
    try:
        data = await bridge.health()
        data["targetDocument"] = bridge.target_document()
        return as_json(data)
    except bridge.BridgeError as exc:
        return as_json({"ok": False, "error": str(exc), "liveInstances": bridge.discover_instances()})


@mcp.tool(name="civil3d_list_instances", annotations={**READ_ANN, "title": "List running Civil 3D sessions"})
async def civil3d_list_instances() -> str:
    """List every running Civil 3D session with the bridge loaded (release, pid, port),
    and which one calls currently go to. Use when 2025 and 2027 are open side by side."""
    return as_json({
        "current": bridge.bridge_url(),
        "targetDocument": bridge.target_document(),
        "instances": bridge.discover_instances(),
    })


@mcp.tool(name="civil3d_select_instance", annotations={**READ_ANN, "title": "Select Civil 3D session"})
async def civil3d_select_instance(params: SelectInstance) -> str:
    """Pin all following calls to one Civil 3D session, by release (e.g. 2027),
    process id, port or URL. Pass clear=true to return to automatic selection."""
    if params.clear:
        bridge.clear_selection()
        return as_json({"pinned": False, "current": bridge.bridge_url()})
    try:
        return as_json(bridge.select_instance(release=params.release, pid=params.pid, port=params.port, url=params.url))
    except bridge.BridgeError as exc:
        return as_json({"error": str(exc)})


@mcp.tool(name="civil3d_select_document", annotations={**READ_ANN, "title": "Target an open drawing"})
async def civil3d_select_document(params: SelectDocument) -> str:
    """Route following calls to a specific open drawing (file name or full path)
    without switching windows. Omit `document` to go back to the active drawing."""
    bridge.set_target_document(params.document)
    return as_json({"targetDocument": bridge.target_document()})


@mcp.tool(name="civil3d_list_documents", annotations={**READ_ANN, "title": "List open drawings"})
async def civil3d_list_documents() -> str:
    """Open drawings in the selected Civil 3D session, the active one, and any command in progress."""
    return await call("civil3d_list_documents")


@mcp.tool(name="civil3d_activate_document", annotations={**WRITE_ANN, "title": "Activate drawing window", "destructiveHint": False})
async def civil3d_activate_document(params: ActivateDocument) -> str:
    """Bring an open drawing to the front (makes it the active MDI document)."""
    return await call("civil3d_activate_document", params.model_dump())


@mcp.tool(name="civil3d_get_drawing_settings", annotations={**READ_ANN, "title": "Drawing units and coordinate zone"})
async def civil3d_get_drawing_settings() -> str:
    """Drawing units, coordinate system code (e.g. UTM-36N / Arc 1960), angular units,
    scale and extents. Check this before trusting coordinates or volumes."""
    return await call("civil3d_get_drawing_settings")


@mcp.tool(name="civil3d_undo", annotations={**WRITE_ANN, "title": "Undo"})
async def civil3d_undo(params: UndoSteps) -> str:
    """Queue UNDO for N steps in the target drawing (each MCP write is normally one step)."""
    return await call("civil3d_undo", params.model_dump())


@mcp.tool(name="civil3d_call", annotations={**WRITE_ANN, "title": "Generic bridge call (escape hatch)"})
async def civil3d_call(params: GenericCall) -> str:
    """Call any tool registered with the C# bridge by name with raw args.
    Use for bridge tools that have no typed wrapper yet (see civil3d_bridge_health)."""
    return await call(params.tool, params.args)
