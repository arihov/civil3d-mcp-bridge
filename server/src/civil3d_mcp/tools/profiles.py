"""Profile-domain tool wrappers — list, inspect, vertical curves, layout profiles."""

from __future__ import annotations

from civil3d_mcp._instance import READ_ANN, WRITE_ANN, call, mcp
from civil3d_mcp.models import (
    AddPvi,
    AddVerticalCurve,
    CreateProfileView,
    ElevationAtStation,
    LayoutProfile,
    ProfileFromSurface,
    ProfileInfo,
    ProfilesByAlignment,
    SampleSurfaceAlongAlignment,
)


@mcp.tool(name="civil3d_list_profiles",
          annotations={**READ_ANN, "title": "List profiles on an alignment"})
async def civil3d_list_profiles(params: ProfilesByAlignment) -> str:
    """List the profiles on a given alignment (EG, FG, layout, etc.)."""
    return await call("civil3d_list_profiles", params.model_dump())


@mcp.tool(name="civil3d_get_profile_info",
          annotations={**READ_ANN, "title": "Profile entity + PVI table"})
async def civil3d_get_profile_info(params: ProfileInfo) -> str:
    """Get the entity list and PVI table for one profile. Call this before
    civil3d_add_vertical_curve to see which PVIs exist."""
    return await call("civil3d_get_profile_info", params.model_dump())


@mcp.tool(name="civil3d_add_vertical_curve",
          annotations={**WRITE_ANN, "title": "Insert vertical curve"})
async def civil3d_add_vertical_curve(params: AddVerticalCurve) -> str:
    """Insert a fixed-length parabolic vertical curve at a PVI. Sag/crest
    inferred from surrounding grades; rolls back if expected_curve_type
    is supplied and the inferred type doesn't match."""
    return await call("civil3d_add_vertical_curve", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_profile_from_surface",
          annotations={**WRITE_ANN, "title": "Sample EG profile from surface"})
async def civil3d_create_profile_from_surface(params: ProfileFromSurface) -> str:
    """Standard EG profile creation — sample a surface along an alignment."""
    return await call("civil3d_create_profile_from_surface", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_create_layout_profile",
          annotations={**WRITE_ANN, "title": "Empty layout profile"})
async def civil3d_create_layout_profile(params: LayoutProfile) -> str:
    """Create an empty layout (design / FG) profile. Add PVIs with civil3d_add_pvi."""
    return await call("civil3d_create_layout_profile", params.model_dump(exclude_none=True))


@mcp.tool(name="civil3d_add_pvi",
          annotations={**WRITE_ANN, "title": "Add PVI"})
async def civil3d_add_pvi(params: AddPvi) -> str:
    """Add a PVI to a layout profile at (station, elevation). Surrounding
    tangents wire up automatically."""
    return await call("civil3d_add_pvi", params.model_dump())


@mcp.tool(name="civil3d_get_elevation_at_station",
          annotations={**READ_ANN, "title": "Profile elevation at station(s)"})
async def civil3d_get_elevation_at_station(params: ElevationAtStation) -> str:
    """Sample profile elevations at one or more stations (batched).
    Useful when sizing culverts — fetch invert-in and invert-out from EG."""
    return await call("civil3d_get_elevation_at_station", params.model_dump())


@mcp.tool(name="civil3d_sample_surface_along_alignment",
          annotations={**READ_ANN, "title": "Sample surface along alignment"})
async def civil3d_sample_surface_along_alignment(params: SampleSurfaceAlongAlignment) -> str:
    """Walk a surface along an alignment at fixed interval, returning
    elevations at the supplied lateral offsets. Used to build cross-section
    summaries without committing to a sample-line group."""
    return await call("civil3d_sample_surface_along_alignment", params.model_dump())


@mcp.tool(name="civil3d_create_profile_view",
          annotations={**WRITE_ANN, "title": "Profile view sheet"})
async def civil3d_create_profile_view(params: CreateProfileView) -> str:
    """Create a profile view at a chosen drawing insertion point. Picks
    first available view + band styles if names not supplied."""
    return await call("civil3d_create_profile_view", params.model_dump(exclude_none=True))
