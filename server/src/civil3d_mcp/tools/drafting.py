"""Styles, plain geometry, measurement schedules, parcels and pressure networks (v0.4)."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import (
    Create3dPolyline,
    CreatePolyline,
    CreateText,
    CsvOnly,
    EraseEntities,
    HandleRef,
    ListStyles,
    MeasurePolylines,
    PressureNetworkRef,
    SetObjectStyle,
)

READ_FILE = {**READ_ANN, "readOnlyHint": False, "destructiveHint": False}


@mcp.tool(name="civil3d_list_styles", annotations={**READ_ANN, "title": "List styles"})
async def civil3d_list_styles(params: ListStyles) -> str:
    """Style names per collection (alignment, profile, surface, profile_view, section_view,
    point, corridor, label sets ...). Omit `collection` for a summary of all."""
    return await call("civil3d_list_styles", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_set_object_style", annotations={**WRITE_ANN, "title": "Set object style"})
async def civil3d_set_object_style(params: SetObjectStyle) -> str:
    """Apply a named style to an alignment, profile, surface, corridor, or any styled object by handle."""
    return await call("civil3d_set_object_style", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_polyline", annotations={**WRITE_ANN, "title": "Create polyline"})
async def civil3d_create_polyline(params: CreatePolyline) -> str:
    """Create a 2D polyline (optionally closed) from [x, y] points."""
    return await call("civil3d_create_polyline", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_3d_polyline", annotations={**WRITE_ANN, "title": "Create 3D polyline"})
async def civil3d_create_3d_polyline(params: Create3dPolyline) -> str:
    """Create a 3D polyline from [x, y, z] points (e.g. for breaklines)."""
    return await call("civil3d_create_3d_polyline", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_text", annotations={**WRITE_ANN, "title": "Create text"})
async def civil3d_create_text(params: CreateText) -> str:
    """Place single-line text or MText."""
    return await call("civil3d_create_text", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_measure_polylines", annotations={**READ_ANN, "title": "Polyline length/area schedule"})
async def civil3d_measure_polylines(params: MeasurePolylines) -> str:
    """Total length and area of polylines grouped by layer, with wildcard layer filters.
    Quick BOQ take-off for kerbs, drains, edge lines and closed pavement areas."""
    return await call("civil3d_measure_polylines", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_get_entity_info", annotations={**READ_ANN, "title": "Entity info by handle"})
async def civil3d_get_entity_info(params: HandleRef) -> str:
    """Type, layer, name, extents, length and area for any object handle."""
    return await call("civil3d_get_entity_info", params.model_dump())


@mcp.tool(name="civil3d_erase_entities", annotations={**WRITE_ANN, "title": "Erase entities"})
async def civil3d_erase_entities(params: EraseEntities) -> str:
    """Erase objects by handle. Reversible with civil3d_undo until the drawing is closed."""
    return await call("civil3d_erase_entities", params.model_dump())


@mcp.tool(name="civil3d_list_sites_parcels", annotations={**READ_FILE, "title": "Sites and parcels"})
async def civil3d_list_sites_parcels(params: CsvOnly) -> str:
    """Sites with parcel number, area (m2 and ha) and perimeter; optional CSV for land schedules."""
    return await call("civil3d_list_sites_parcels", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_list_pressure_networks", annotations={**READ_ANN, "title": "List pressure networks"})
async def civil3d_list_pressure_networks() -> str:
    """Pressure (water) networks with pipe, fitting and appurtenance counts."""
    return await call("civil3d_list_pressure_networks")


@mcp.tool(name="civil3d_get_pressure_network_schedule", annotations={**READ_FILE, "title": "Pressure pipe schedule"})
async def civil3d_get_pressure_network_schedule(params: PressureNetworkRef) -> str:
    """Pipe lengths grouped by part and nominal diameter; optional CSV."""
    return await call("civil3d_get_pressure_network_schedule", params.model_dump(exclude_none=True))
