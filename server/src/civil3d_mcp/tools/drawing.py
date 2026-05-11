"""Drawing-level utility tool wrappers — save, zoom, layers, run command."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import CreateLayer, LayerName, RunCommand, SaveDrawing, ZoomToAlignment


@mcp.tool(name="civil3d_get_drawing_info",
          annotations={**READ_ANN, "title": "Drawing info"})
async def civil3d_get_drawing_info() -> str:
    """Drawing name, filename, version, object counts (alignments, corridors, etc.)."""
    return await call("civil3d_get_drawing_info")


@mcp.tool(name="civil3d_save_drawing",
          annotations={**WRITE_ANN, "title": "Save drawing",
                       "idempotentHint": True})
async def civil3d_save_drawing(params: SaveDrawing) -> str:
    """Save the active drawing. Path triggers SAVEAS (always Civil 3D 2018+
    format), otherwise QSAVE."""
    return await call("civil3d_save_drawing", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_zoom_to_alignment",
          annotations={**WRITE_ANN, "title": "Zoom to alignment",
                       "destructiveHint": False, "idempotentHint": True})
async def civil3d_zoom_to_alignment(params: ZoomToAlignment) -> str:
    """Zoom the active viewport to the extents of an alignment, padded 10%."""
    return await call("civil3d_zoom_to_alignment", params.model_dump())


@mcp.tool(name="civil3d_zoom_extents",
          annotations={**WRITE_ANN, "title": "Zoom extents",
                       "destructiveHint": False, "idempotentHint": True})
async def civil3d_zoom_extents() -> str:
    """Zoom the active viewport to drawing extents."""
    return await call("civil3d_zoom_extents")


@mcp.tool(name="civil3d_list_layers",
          annotations={**READ_ANN, "title": "List layers"})
async def civil3d_list_layers() -> str:
    """List all layers with state flags (frozen / off / locked / current)."""
    return await call("civil3d_list_layers")


@mcp.tool(name="civil3d_create_layer",
          annotations={**WRITE_ANN, "title": "Create layer"})
async def civil3d_create_layer(params: CreateLayer) -> str:
    """Create a layer with a given colour index. Idempotent — reports
    alreadyExists=true if the layer is already in the LayerTable."""
    return await call("civil3d_create_layer", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_set_current_layer",
          annotations={**WRITE_ANN, "title": "Set current layer"})
async def civil3d_set_current_layer(params: LayerName) -> str:
    """Make a layer the drawing's current/active layer."""
    return await call("civil3d_set_current_layer", params.model_dump())


@mcp.tool(name="civil3d_run_command",
          annotations={**WRITE_ANN, "title": "Run AutoCAD/C3D command (escape hatch)"})
async def civil3d_run_command(params: RunCommand) -> str:
    """ESCAPE HATCH: send an arbitrary AutoCAD/C3D command-line string to
    the document, exactly as if typed. No return value beyond confirmation
    that it was queued. Use for anything not directly wrapped — running an
    AutoLISP routine, invoking a SCRIPT, etc. The command must end with a
    space (Enter)."""
    return await call("civil3d_run_command", params.model_dump())
