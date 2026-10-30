"""Surfaces, feature lines, earthworks, sections and corridor surfaces (v0.4)."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import (
    CorridorOnly,
    CreateCorridorSurface,
    CreateFeatureLine,
    CreateSectionViews,
    EarthworkBySections,
    ExportSections,
    ExtractContours,
    FeatureLineFromSurface,
    FeatureLinePoints,
    SampleLineGroupRef,
    SurfaceBoundary,
    SurfaceOnly,
    SurfaceSlopes,
    SurfaceTransect,
)

READ_FILE = {**READ_ANN, "readOnlyHint": False, "destructiveHint": False}  # may write a CSV, never the DWG
LONG = 900.0  # seconds; big swath/section jobs on long corridors


# ─── earthworks ───────────────────────────────────────────────────────────

@mcp.tool(name="civil3d_earthwork_by_sections", annotations={**READ_FILE, "title": "Earthworks by average end area"})
async def civil3d_earthwork_by_sections(params: EarthworkBySections) -> str:
    """Cut/fill volumes between an existing and a design surface along an alignment by
    average end area, sampled straight from the surfaces (no sample lines or QTO setup).
    Returns per-station areas, volumes, mass-haul ordinate and totals; optional CSV.
    Independent check on Civil 3D's Compute Materials."""
    return await call("civil3d_earthwork_by_sections", params.model_dump(exclude_none=True), timeout_s=LONG)


# ─── surfaces ─────────────────────────────────────────────────────────────

@mcp.tool(name="civil3d_get_surface_statistics", annotations={**READ_ANN, "title": "Surface statistics"})
async def civil3d_get_surface_statistics(params: SurfaceOnly) -> str:
    """Point count, min/max/mean elevation, extents, 2D/3D area and grade statistics."""
    return await call("civil3d_get_surface_statistics", params.model_dump())


@mcp.tool(name="civil3d_analyze_surface_slopes", annotations={**READ_ANN, "title": "Slope and elevation banding"})
async def civil3d_analyze_surface_slopes(params: SurfaceSlopes) -> str:
    """Area-weighted slope bands (%) and optional elevation bands from the TIN triangles."""
    return await call("civil3d_analyze_surface_slopes", params.model_dump(exclude_none=True), timeout_s=LONG)


@mcp.tool(name="civil3d_sample_surface_transect", annotations={**READ_ANN, "title": "Surface transect"})
async def civil3d_sample_surface_transect(params: SurfaceTransect) -> str:
    """Elevations along a line (start/end/step) or at a list of points."""
    return await call("civil3d_sample_surface_transect", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_add_surface_boundary", annotations={**WRITE_ANN, "title": "Add surface boundary"})
async def civil3d_add_surface_boundary(params: SurfaceBoundary) -> str:
    """Add an outer / hide / show / data-clip boundary from a closed polyline and rebuild."""
    return await call("civil3d_add_surface_boundary", params.model_dump())


@mcp.tool(name="civil3d_extract_surface_contours", annotations={**WRITE_ANN, "title": "Extract contours"})
async def civil3d_extract_surface_contours(params: ExtractContours) -> str:
    """Extract contour polylines at an interval, optionally onto a layer."""
    return await call("civil3d_extract_surface_contours", params.model_dump(exclude_none=True), timeout_s=LONG)


@mcp.tool(name="civil3d_rebuild_surface", annotations={**WRITE_ANN, "title": "Rebuild surface", "destructiveHint": False})
async def civil3d_rebuild_surface(params: SurfaceOnly) -> str:
    """Rebuild an out-of-date surface."""
    return await call("civil3d_rebuild_surface", params.model_dump(), timeout_s=LONG)


# ─── feature lines ────────────────────────────────────────────────────────

@mcp.tool(name="civil3d_list_feature_lines", annotations={**READ_ANN, "title": "List feature lines"})
async def civil3d_list_feature_lines() -> str:
    """Feature lines in all sites and site-less, with 2D/3D length and elevation range."""
    return await call("civil3d_list_feature_lines")


@mcp.tool(name="civil3d_create_feature_line", annotations={**WRITE_ANN, "title": "Create feature line"})
async def civil3d_create_feature_line(params: CreateFeatureLine) -> str:
    """Create a feature line from an existing polyline (handle) or from [x, y, z] points."""
    return await call("civil3d_create_feature_line", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_feature_line_elevations_from_surface", annotations={**WRITE_ANN, "title": "Drape feature line on surface"})
async def civil3d_feature_line_elevations_from_surface(params: FeatureLineFromSurface) -> str:
    """Assign feature line elevations from a surface, optionally inserting grade breaks."""
    return await call("civil3d_feature_line_elevations_from_surface", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_get_feature_line_points", annotations={**READ_FILE, "title": "Feature line vertices"})
async def civil3d_get_feature_line_points(params: FeatureLinePoints) -> str:
    """All PI and elevation points with running 2D chainage; optional CSV."""
    return await call("civil3d_get_feature_line_points", params.model_dump(exclude_none=True))


# ─── sections ─────────────────────────────────────────────────────────────

@mcp.tool(name="civil3d_list_sample_lines", annotations={**READ_ANN, "title": "List sample lines"})
async def civil3d_list_sample_lines(params: SampleLineGroupRef) -> str:
    """Sample lines in a group with station, swath offsets and section count."""
    return await call("civil3d_list_sample_lines", params.model_dump())


@mcp.tool(name="civil3d_export_sampled_sections", annotations={**READ_FILE, "title": "Export cross-section data"})
async def civil3d_export_sampled_sections(params: ExportSections) -> str:
    """Offset/elevation points of every sampled section (EG, FG, corridor surfaces) per station;
    write a CSV for cross-section QA or third-party volume checks."""
    return await call("civil3d_export_sampled_sections", params.model_dump(exclude_none=True), timeout_s=LONG)


@mcp.tool(name="civil3d_create_section_views", annotations={**WRITE_ANN, "title": "Create section views"})
async def civil3d_create_section_views(params: CreateSectionViews) -> str:
    """Create one section view per sample line, laid out in a grid from an origin."""
    return await call("civil3d_create_section_views", params.model_dump(exclude_none=True), timeout_s=LONG)


# ─── corridor surfaces ────────────────────────────────────────────────────

@mcp.tool(name="civil3d_get_corridor_codes", annotations={**READ_ANN, "title": "Corridor codes"})
async def civil3d_get_corridor_codes(params: CorridorOnly) -> str:
    """Link, point and shape codes present in a corridor (needed to build surfaces)."""
    return await call("civil3d_get_corridor_codes", params.model_dump())


@mcp.tool(name="civil3d_get_corridor_regions", annotations={**READ_ANN, "title": "Corridor baselines and regions"})
async def civil3d_get_corridor_regions(params: CorridorOnly) -> str:
    """Baselines (alignment/profile) and regions with station ranges and assemblies."""
    return await call("civil3d_get_corridor_regions", params.model_dump())


@mcp.tool(name="civil3d_list_corridor_surfaces", annotations={**READ_ANN, "title": "List corridor surfaces"})
async def civil3d_list_corridor_surfaces(params: CorridorOnly) -> str:
    """Corridor surfaces with their link / feature line codes and boundaries."""
    return await call("civil3d_list_corridor_surfaces", params.model_dump())


@mcp.tool(name="civil3d_create_corridor_surface", annotations={**WRITE_ANN, "title": "Create corridor surface"})
async def civil3d_create_corridor_surface(params: CreateCorridorSurface) -> str:
    """Create a corridor surface (e.g. Top for finished grade, Datum for formation) from link
    codes, add a corridor-extents boundary and rebuild."""
    return await call("civil3d_create_corridor_surface", params.model_dump(), timeout_s=LONG)
