"""Surface-domain tool wrappers."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import (
    AddPointsToSurface,
    BreaklineFromPolyline,
    CreateTinSurface,
    ElevationAtXy,
    PersistentVolume,
    SurfaceFromPoints,
    SurfaceVolume,
)


@mcp.tool(name="civil3d_list_surfaces",
          annotations={**READ_ANN, "title": "List surfaces"})
async def civil3d_list_surfaces() -> str:
    """List TIN/grid surfaces with elevation range and 2D/3D area."""
    return await call("civil3d_list_surfaces")


@mcp.tool(name="civil3d_get_surface_volume",
          annotations={**WRITE_ANN, "title": "Cut/fill volume",
                       "destructiveHint": False})
async def civil3d_get_surface_volume(params: SurfaceVolume) -> str:
    """Compute cut/fill/net volume between two TIN surfaces. Temporary
    volume surface is created and erased unless `keep=true`."""
    return await call("civil3d_get_surface_volume", params.model_dump())


@mcp.tool(name="civil3d_get_elevation_at_xy",
          annotations={**READ_ANN, "title": "Elevation at (X,Y)"})
async def civil3d_get_elevation_at_xy(params: ElevationAtXy) -> str:
    """Sample a surface elevation at a world (X, Y). Returns null if outside hull."""
    return await call("civil3d_get_elevation_at_xy", params.model_dump())


@mcp.tool(name="civil3d_create_tin_surface",
          annotations={**WRITE_ANN, "title": "Create empty TIN surface"})
async def civil3d_create_tin_surface(params: CreateTinSurface) -> str:
    """Create an empty TIN surface. Add data sources separately with
    civil3d_add_points_to_surface or civil3d_add_breakline_from_polyline."""
    return await call("civil3d_create_tin_surface", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_surface_from_points",
          annotations={**WRITE_ANN, "title": "TIN surface from points"})
async def civil3d_create_surface_from_points(params: SurfaceFromPoints) -> str:
    """One-shot: create a TIN surface from a named point group (or all
    CogoPoints if no group specified) and rebuild."""
    return await call("civil3d_create_surface_from_points", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_add_points_to_surface",
          annotations={**WRITE_ANN, "title": "Add points to surface"})
async def civil3d_add_points_to_surface(params: AddPointsToSurface) -> str:
    """Attach a point group (or all CogoPoints) as a data source on an
    existing TIN surface, then rebuild it."""
    return await call("civil3d_add_points_to_surface", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_add_breakline_from_polyline",
          annotations={**WRITE_ANN, "title": "Add breakline"})
async def civil3d_add_breakline_from_polyline(params: BreaklineFromPolyline) -> str:
    """Add an AutoCAD polyline as a breakline on a TIN surface. The polyline
    is identified by its hex handle."""
    return await call("civil3d_add_breakline_from_polyline", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_persistent_volume_surface",
          annotations={**WRITE_ANN, "title": "Persistent volume surface"})
async def civil3d_create_persistent_volume_surface(params: PersistentVolume) -> str:
    """Create a permanent TIN volume surface between two existing surfaces
    (lives in the drawing for ongoing cut/fill tracking)."""
    return await call("civil3d_create_persistent_volume_surface", params.model_dump())
