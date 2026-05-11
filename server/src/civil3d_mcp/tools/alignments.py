"""Alignment-domain tool wrappers."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import (
    AlignmentByPoly,
    AlignmentName,
    DesignSpeed,
    ExportGeometry,
    OffsetAlignment,
    PointAtChainage,
    StationAtXy,
    XyAtStation,
)


@mcp.tool(name="civil3d_list_alignments",
          annotations={**READ_ANN, "title": "List alignments"})
async def civil3d_list_alignments() -> str:
    """List every alignment in the active drawing with stationing and style."""
    return await call("civil3d_list_alignments")


@mcp.tool(name="civil3d_get_alignment_info",
          annotations={**READ_ANN, "title": "Alignment detail"})
async def civil3d_get_alignment_info(params: AlignmentName) -> str:
    """Geometry entities, station equations, design speeds, child profile names."""
    return await call("civil3d_get_alignment_info", params.model_dump())


@mcp.tool(name="civil3d_point_at_chainage",
          annotations={**READ_ANN, "title": "Point at chainage"})
async def civil3d_point_at_chainage(params: PointAtChainage) -> str:
    """Return (easting, northing, optional elevation, tangent direction) at
    a given alignment station and lateral offset."""
    return await call("civil3d_point_at_chainage", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_get_xy_at_station",
          annotations={**READ_ANN, "title": "XY from station"})
async def civil3d_get_xy_at_station(params: XyAtStation) -> str:
    """Just the (easting, northing) for (alignment, station, offset)."""
    return await call("civil3d_get_xy_at_station", params.model_dump())


@mcp.tool(name="civil3d_get_station_at_xy",
          annotations={**READ_ANN, "title": "Station from XY"})
async def civil3d_get_station_at_xy(params: StationAtXy) -> str:
    """Reverse lookup — given a world (X, Y), return the station + perpendicular
    offset on the given alignment."""
    return await call("civil3d_get_station_at_xy", params.model_dump())


@mcp.tool(name="civil3d_create_alignment_from_polyline",
          annotations={**WRITE_ANN, "title": "Alignment from polyline"})
async def civil3d_create_alignment_from_polyline(params: AlignmentByPoly) -> str:
    """Convert an existing AutoCAD polyline (identified by its handle) into
    a Civil 3D alignment. Optionally erase the polyline."""
    return await call("civil3d_create_alignment_from_polyline", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_offset_alignment",
          annotations={**WRITE_ANN, "title": "Offset alignment"})
async def civil3d_create_offset_alignment(params: OffsetAlignment) -> str:
    """Create a constant-offset alignment from a parent (e.g. lane edge from CL)."""
    return await call("civil3d_create_offset_alignment", params.model_dump())


@mcp.tool(name="civil3d_set_design_speed",
          annotations={**WRITE_ANN, "title": "Set design speed"})
async def civil3d_set_design_speed(params: DesignSpeed) -> str:
    """Add or replace a design speed entry on an alignment."""
    return await call("civil3d_set_design_speed", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_export_alignment_geometry",
          annotations={**READ_ANN, "title": "Export alignment to CSV"})
async def civil3d_export_alignment_geometry(params: ExportGeometry) -> str:
    """Dump alignment entity table (line/curve/spiral) to CSV for external review."""
    return await call("civil3d_export_alignment_geometry", params.model_dump())
