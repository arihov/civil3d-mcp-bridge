"""Roundabout geometry tools."""

from __future__ import annotations

from civil3d_mcp._instance import WRITE_ANN, call, mcp
from civil3d_mcp.models import RoundaboutCenterline


@mcp.tool(name="civil3d_create_roundabout_centerline",
          annotations={**WRITE_ANN, "title": "Create roundabout centreline"})
async def civil3d_create_roundabout_centerline(params: RoundaboutCenterline) -> str:
    """Build a closed circular Civil 3D alignment (the circulating-carriageway
    centreline) from an exact 4-quadrant circle, and — if lane_width > 0 — inner
    and outer kerb offset alignments at +/- lane_width/2. Use the existing
    corridor + assembly tools to add the carriageway surface, and
    civil3d_draw_lane_marking to add lane lines around it. Superelevation and
    native roundabout markings are a manual / follow-up step."""
    return await call("civil3d_create_roundabout_centerline", params.model_dump(exclude_none=True))
