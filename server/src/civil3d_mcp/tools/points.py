"""COGO point + point-group tool wrappers."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import (
    CreatePoint,
    CreatePointGroup,
    ListPoints,
    PointsCsv,
    PointsCsvIn,
)


@mcp.tool(name="civil3d_list_points",
          annotations={**READ_ANN, "title": "List COGO points (paginated)"})
async def civil3d_list_points(params: ListPoints = ListPoints()) -> str:
    """Paginated list of COGO points. Returns total + nextOffset for batching."""
    return await call("civil3d_list_points", params.model_dump())


@mcp.tool(name="civil3d_list_point_groups",
          annotations={**READ_ANN, "title": "List COGO point groups"})
async def civil3d_list_point_groups() -> str:
    """List point groups in the drawing."""
    return await call("civil3d_list_point_groups")


@mcp.tool(name="civil3d_create_point",
          annotations={**WRITE_ANN, "title": "Create COGO point"})
async def civil3d_create_point(params: CreatePoint) -> str:
    """Create one COGO point at (northing, easting, elevation)."""
    return await call("civil3d_create_point", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_import_points_csv",
          annotations={**WRITE_ANN, "title": "Bulk import COGO points from CSV"})
async def civil3d_import_points_csv(params: PointsCsvIn) -> str:
    """Bulk-create COGO points from CSV (PNEZD/PENZD with header detection).
    Returns created/skipped counts plus first 10 error rows."""
    return await call("civil3d_import_points_csv", params.model_dump())


@mcp.tool(name="civil3d_export_points_csv",
          annotations={**WRITE_ANN, "title": "Export COGO points to CSV",
                       "destructiveHint": False})
async def civil3d_export_points_csv(params: PointsCsv) -> str:
    """Export all COGO points to CSV in PNEZD format."""
    return await call("civil3d_export_points_csv", params.model_dump())


@mcp.tool(name="civil3d_create_point_group",
          annotations={**WRITE_ANN, "title": "Create point group"})
async def civil3d_create_point_group(params: CreatePointGroup) -> str:
    """Create a point group, optionally filtered by raw-description glob
    (e.g. 'IP-*' for intersection points only)."""
    return await call("civil3d_create_point_group", params.model_dump(exclude_none=True))
