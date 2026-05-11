"""Corridor-domain tool wrappers."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import (
    AddBaselineRegion,
    CorridorName,
    CreateCorridor,
    ExportCorridorSections,
    SetRegionAssembly,
    SetRegionTargetSurface,
)


@mcp.tool(name="civil3d_list_corridors",
          annotations={**READ_ANN, "title": "List corridors"})
async def civil3d_list_corridors() -> str:
    """List corridors with baseline count and out-of-date flag."""
    return await call("civil3d_list_corridors")


@mcp.tool(name="civil3d_get_corridor_info",
          annotations={**READ_ANN, "title": "Corridor baselines + regions"})
async def civil3d_get_corridor_info(params: CorridorName) -> str:
    """Full baseline/region/assembly map of a corridor. Use this to find
    the (baseline_index, region_index) pair before any region edit."""
    return await call("civil3d_get_corridor_info", params.model_dump())


@mcp.tool(name="civil3d_list_assemblies",
          annotations={**READ_ANN, "title": "List assemblies"})
async def civil3d_list_assemblies() -> str:
    """List corridor assemblies available in the drawing."""
    return await call("civil3d_list_assemblies")


@mcp.tool(name="civil3d_create_corridor",
          annotations={**WRITE_ANN, "title": "Create corridor"})
async def civil3d_create_corridor(params: CreateCorridor) -> str:
    """Create a corridor from (alignment, profile, assembly). Optionally
    set a target surface for daylighting subassemblies in the same call."""
    return await call("civil3d_create_corridor", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_add_baseline_region",
          annotations={**WRITE_ANN, "title": "Add corridor region"})
async def civil3d_add_baseline_region(params: AddBaselineRegion) -> str:
    """Append a region (with assembly) to an existing baseline. Used when
    splitting a corridor into cross-section zones along its length."""
    return await call("civil3d_add_baseline_region", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_set_region_assembly",
          annotations={**WRITE_ANN, "title": "Swap region assembly"})
async def civil3d_set_region_assembly(params: SetRegionAssembly) -> str:
    """Apply a different assembly to a region — standard urban→rural transition."""
    return await call("civil3d_set_region_assembly", params.model_dump())


@mcp.tool(name="civil3d_set_region_target_surface",
          annotations={**WRITE_ANN, "title": "Set region target surface"})
async def civil3d_set_region_target_surface(params: SetRegionTargetSurface) -> str:
    """Bind a named subassembly target (typically 'Daylight_Target') on a
    region to an existing surface. Required for the corridor to daylight."""
    return await call("civil3d_set_region_target_surface", params.model_dump())


@mcp.tool(name="civil3d_rebuild_corridor",
          annotations={**WRITE_ANN, "title": "Rebuild corridor",
                       "idempotentHint": True})
async def civil3d_rebuild_corridor(params: CorridorName) -> str:
    """Rebuild a corridor's model. Slow on large corridors (~tens of seconds)."""
    return await call("civil3d_rebuild_corridor", params.model_dump())


@mcp.tool(name="civil3d_export_corridor_sections",
          annotations={**WRITE_ANN, "title": "Export corridor sections to CSV",
                       "destructiveHint": False})
async def civil3d_export_corridor_sections(params: ExportCorridorSections) -> str:
    """Sample cross-sections at fixed station interval and dump every
    corridor point code to CSV (station, point_code, offset, elevation, easting, northing)."""
    return await call("civil3d_export_corridor_sections", params.model_dump())
